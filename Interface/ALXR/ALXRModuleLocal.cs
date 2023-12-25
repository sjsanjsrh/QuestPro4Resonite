using Elements.Core;
using FrooxEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LibALXR;
using ResoniteModLoader;

namespace QuestProModule.ALXR
{
    public class ALXRModuleLocal : IQuestProModule
    {
        private CancellationTokenSource cancellationTokenSource;
        private bool _connected = false;

        private bool InvertJaw;

        private const float SRANIPAL_NORMALIZER = 0.75f;
        private ALXRProcessFrameResult alxrResult;
        private float[] expressions = new float[(int)FBExpression2.Max];

        private double pitch_L, yaw_L, pitch_R, yaw_R; // Eye rotations

        private Thread updateThread;
        public bool Connected => _connected;

        public class Config
        {
            public string DLLDir { get; set; } = "";
            public ALXRGraphicsApi GraphicsApi { get; set; } = ALXRGraphicsApi.Auto;
            public ALXRFacialExpressionType FacialTrackingExt { get; set; } = ALXRFacialExpressionType.FB_V2;
            public ALXREyeTrackingType EyeTrackingExt { get; set; } = ALXREyeTrackingType.Auto;
            public bool EnableHandleTracking { get; set; } = true;
        }
        static Config config = new Config();

        public Task<bool> Initialize(string dlldir)
        {
            //if (!LibALXR.LibALXR.AddDllSearchPath(dlldir))
            //{
            //    ResoniteMod.Error($"unmanaged laxrlib path to search failed to be set.");
            //    return Task.FromResult(false);
            //}
            //config.DLLDir = dlldir;
            try
            {
                cancellationTokenSource = new CancellationTokenSource();

                updateThread = new Thread(Update);
                updateThread.Start();
            }
            catch (Exception e)
            {
                ResoniteMod.Error("Exception when initializing Quest Pro");
                UniLog.Error(e.Message);
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        private bool ConnectALXR()
        {
            try
            {
                var ctx = CreateALXRClientCtx();
                var sysProperties = new ALXRSystemProperties();
                if (!LibALXR.LibALXR.alxr_init(ref ctx, out sysProperties))
                {
                    return false;
                }

                LibALXR.LibALXR.alxr_set_log_custom_output(ALXRLogOptions.None, (level, output, len) =>
                {
                    var fullMsg = $"[libalxr] {output}";
                    switch (level)
                    {
                        case ALXRLogLevel.Info:
                            ResoniteMod.Msg(fullMsg);
                            break;
                        case ALXRLogLevel.Warning:
                            ResoniteMod.Warn(fullMsg);
                            break;
                        case ALXRLogLevel.Error:
                            ResoniteMod.Error(fullMsg);
                            break;
                    }
                });
            }
            catch (Exception e)
            {
                ResoniteMod.Error("Exception when connecting to Quest Pro");
                UniLog.Error(e.Message);
                return false;
            }
            return true;
        }

        public void Update()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (!ConnectALXR())
                {
                    ResoniteMod.Error("failed connect to Quest Pro");
                    Thread.Sleep(1000);
                }
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        LibALXR.LibALXR.alxr_process_frame2(ref alxrResult);
                        alxrResult.facialEyeTracking.ExpressionWeightSpan.CopyTo(expressions);
                        if (!LibALXR.LibALXR.alxr_is_session_running())
                        {
                            Thread.Sleep(250);
                            _connected = false;
                        }
                        else
                        {
                            _connected = true;
                        }
                        // Preprocess our expressions per Meta's Documentation
                        PrepareUpdate();
                    }
                    catch (SocketException e)
                    {
                        ResoniteMod.Error("SocketException when updating Quest Pro");
                        UniLog.Error(e.Message);
                        _connected = false;
                        Thread.Sleep(1000);
                    }
                }
            }
            ResoniteMod.Warn("update thread exited");
        }
    
        private void PrepareUpdate()
        {
            // Eye Expressions

            var q = alxrResult.facialEyeTracking.eyeGazePose0.orientation;

            // From radians
            pitch_L = 180.0 / Math.PI * q.Pitch; 
            yaw_L = 180.0 / Math.PI * q.Yaw;

            q = alxrResult.facialEyeTracking.eyeGazePose1.orientation;

            // From radians
            pitch_R = 180.0 / Math.PI * q.Pitch;
            yaw_R = 180.0 / Math.PI * q.Yaw;

            // Face Expressions

            // Eyelid edge case, eyes are actually closed now
            if (expressions[(int)FBExpression2.Eyes_Look_Down_L] == expressions[(int)FBExpression2.Eyes_Look_up_L] && expressions[(int)FBExpression2.Eyes_Closed_L] > 0.25f)
            { 
                expressions[(int)FBExpression2.Eyes_Closed_L] = 0; // 0.9f - (expressions[(int)FBExpression2.Lid_Tightener_L] * 3);
            }
            else
            {
                expressions[(int)FBExpression2.Eyes_Closed_L] = 0.9f - ((expressions[(int)FBExpression2.Eyes_Closed_L] * 3) / (1 + expressions[(int)FBExpression2.Eyes_Look_Down_L] * 3));
            }

            // Another eyelid edge case
            if (expressions[(int)FBExpression2.Eyes_Look_Down_R] == expressions[(int)FBExpression2.Eyes_Look_up_R] && expressions[(int)FBExpression2.Eyes_Closed_R] > 0.25f)
            { 
                expressions[(int)FBExpression2.Eyes_Closed_R] = 0; // 0.9f - (expressions[(int)FBExpression2.Lid_Tightener_R] * 3);
            }
            else
            {
                expressions[(int)FBExpression2.Eyes_Closed_R] = 0.9f - ((expressions[(int)FBExpression2.Eyes_Closed_R] * 3) / (1 + expressions[(int)FBExpression2.Eyes_Look_Down_R] * 3));
            }

            //expressions[(int)FBExpression2.Lid_Tightener_L = 0.8f-expressions[(int)FBExpression2.Eyes_Closed_L]; // Sad: fix combined param instead
            //expressions[(int)FBExpression2.Lid_Tightener_R = 0.8f-expressions[(int)FBExpression2.Eyes_Closed_R]; // Sad: fix combined param instead

            if (1 - expressions[(int)FBExpression2.Eyes_Closed_L] < expressions[(int)FBExpression2.Lid_Tightener_L])
                expressions[(int)FBExpression2.Lid_Tightener_L] = (1 - expressions[(int)FBExpression2.Eyes_Closed_L]) - 0.01f;

            if (1 - expressions[(int)FBExpression2.Eyes_Closed_R] < expressions[(int)FBExpression2.Lid_Tightener_R])
                expressions[(int)FBExpression2.Lid_Tightener_R] = (1 - expressions[(int)FBExpression2.Eyes_Closed_R]) - 0.01f;

            //expressions[(int)FBExpression2.Lid_Tightener_L = Math.Max(0, expressions[(int)FBExpression2.Lid_Tightener_L] - 0.15f);
            //expressions[(int)FBExpression2.Lid_Tightener_R = Math.Max(0, expressions[(int)FBExpression2.Lid_Tightener_R] - 0.15f);

            expressions[(int)FBExpression2.Upper_Lid_Raiser_L] = Math.Max(0, expressions[(int)FBExpression2.Upper_Lid_Raiser_L] - 0.5f);
            expressions[(int)FBExpression2.Upper_Lid_Raiser_R] = Math.Max(0, expressions[(int)FBExpression2.Upper_Lid_Raiser_R] - 0.5f);

            expressions[(int)FBExpression2.Lid_Tightener_L] = Math.Max(0, expressions[(int)FBExpression2.Lid_Tightener_L] - 0.5f);
            expressions[(int)FBExpression2.Lid_Tightener_R] = Math.Max(0, expressions[(int)FBExpression2.Lid_Tightener_R] - 0.5f);

            expressions[(int)FBExpression2.Inner_Brow_Raiser_L] = Math.Min(1, expressions[(int)FBExpression2.Inner_Brow_Raiser_L] * 3f); // * 4;
            expressions[(int)FBExpression2.Brow_Lowerer_L] = Math.Min(1, expressions[(int)FBExpression2.Brow_Lowerer_L] * 3f); // * 4;
            expressions[(int)FBExpression2.Outer_Brow_Raiser_L] = Math.Min(1, expressions[(int)FBExpression2.Outer_Brow_Raiser_L] * 3f); // * 4;

            expressions[(int)FBExpression2.Inner_Brow_Raiser_R] = Math.Min(1, expressions[(int)FBExpression2.Inner_Brow_Raiser_R] * 3f); // * 4;
            expressions[(int)FBExpression2.Brow_Lowerer_R] = Math.Min(1, expressions[(int)FBExpression2.Brow_Lowerer_R] * 3f); // * 4;
            expressions[(int)FBExpression2.Outer_Brow_Raiser_R] = Math.Min(1, expressions[(int)FBExpression2.Outer_Brow_Raiser_R] * 3f); // * 4;

            expressions[(int)FBExpression2.Eyes_Look_up_L] = expressions[(int)FBExpression2.Eyes_Look_up_L] * 0.55f;
            expressions[(int)FBExpression2.Eyes_Look_up_R] = expressions[(int)FBExpression2.Eyes_Look_up_R] * 0.55f;
            expressions[(int)FBExpression2.Eyes_Look_Down_L] = expressions[(int)FBExpression2.Eyes_Look_Down_L] * 1.5f;
            expressions[(int)FBExpression2.Eyes_Look_Down_R] = expressions[(int)FBExpression2.Eyes_Look_Down_R] * 1.5f;

            expressions[(int)FBExpression2.Eyes_Look_Left_L] = expressions[(int)FBExpression2.Eyes_Look_Left_L] * 0.85f;
            expressions[(int)FBExpression2.Eyes_Look_Right_L] = expressions[(int)FBExpression2.Eyes_Look_Right_L] * 0.85f;
            expressions[(int)FBExpression2.Eyes_Look_Left_R] = expressions[(int)FBExpression2.Eyes_Look_Left_R] * 0.85f;
            expressions[(int)FBExpression2.Eyes_Look_Right_R] = expressions[(int)FBExpression2.Eyes_Look_Right_R] * 0.85f;

            // Hack: turn rots to looks
            // Yitch = 29(left)-- > -29(right)
            // Yaw = -27(down)-- > 27(up)

            if (pitch_L > 0)
            {
                expressions[(int)FBExpression2.Eyes_Look_Left_L] = Math.Min(1, (float)(pitch_L / 29.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression2.Eyes_Look_Right_L] = 0;
            }
            else
            {
                expressions[(int)FBExpression2.Eyes_Look_Left_L] = 0;
                expressions[(int)FBExpression2.Eyes_Look_Right_L] = Math.Min(1, (float)((-pitch_L) / 29.0)) * SRANIPAL_NORMALIZER;
            }

            if (yaw_L > 0)
            {
                expressions[(int)FBExpression2.Eyes_Look_up_L] = Math.Min(1, (float)(yaw_L / 27.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression2.Eyes_Look_Down_L] = 0;
            }
            else
            {
                expressions[(int)FBExpression2.Eyes_Look_up_L] = 0;
                expressions[(int)FBExpression2.Eyes_Look_Down_L] = Math.Min(1, (float)((-yaw_L) / 27.0)) * SRANIPAL_NORMALIZER;
            }


            if (pitch_R > 0)
            {
                expressions[(int)FBExpression2.Eyes_Look_Left_R] = Math.Min(1, (float)(pitch_R / 29.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression2.Eyes_Look_Right_R] = 0;
            }
            else
            {
                expressions[(int)FBExpression2.Eyes_Look_Left_R] = 0;
                expressions[(int)FBExpression2.Eyes_Look_Right_R] = Math.Min(1, (float)((-pitch_R) / 29.0)) * SRANIPAL_NORMALIZER;
            }
            
            if (yaw_R > 0)
            {
                expressions[(int)FBExpression2.Eyes_Look_up_R] = Math.Min(1, (float)(yaw_R / 27.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression2.Eyes_Look_Down_R] = 0;
            }
            else
            {
                expressions[(int)FBExpression2.Eyes_Look_up_R] = 0;
                expressions[(int)FBExpression2.Eyes_Look_Down_R] = Math.Min(1, (float)((-yaw_R) / 27.0)) * SRANIPAL_NORMALIZER;
            }
        }

        float3 ALXRTypeToSystem(ALXRVector3f input)
        {
            return new float3(input.x, input.y, input.z);
        }
        floatQ ALXRTypeToSystem(ALXRQuaternionf input)
        {
            return new floatQ(input.x, input.y, input.z, input.w);
        }

        bool IsValid(float3 value) => IsValid(value.x) && IsValid(value.y) && IsValid(value.z);

        bool IsValid(float value) => !float.IsInfinity(value) && !float.IsNaN(value);

        public EyeGazeData GetEyeData(FBEye fbEye)
        {
            EyeGazeData eyeRet = new EyeGazeData();
            switch (fbEye)
            {
                case FBEye.Left:
                    eyeRet.position = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose0.position);
                    eyeRet.rotation = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose0.orientation);
                    eyeRet.open = MathX.Max(0, expressions[(int)FBExpression2.Eyes_Closed_L]);
                    eyeRet.squeeze = expressions[(int)FBExpression2.Lid_Tightener_L];
                    eyeRet.wide = expressions[(int)FBExpression2.Upper_Lid_Raiser_L];
                    eyeRet.isValid = IsValid(eyeRet.position);
                    return eyeRet;
                case FBEye.Right:
                    eyeRet.position = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose1.position);
                    eyeRet.rotation = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose1.orientation);
                    eyeRet.open = MathX.Max(0, expressions[(int)FBExpression2.Eyes_Closed_R]);
                    eyeRet.squeeze = expressions[(int)FBExpression2.Lid_Tightener_R];
                    eyeRet.wide = expressions[(int)FBExpression2.Upper_Lid_Raiser_R];
                    eyeRet.isValid = IsValid(eyeRet.position);
                    return eyeRet;
                default:
                    throw new Exception($"Invalid eye argument: {fbEye}");
            }
        }

        // TODO: Double check jaw movements and mappings
        public void GetFacialExpressions(FrooxEngine.Mouth mouth)
        {
            mouth.IsDeviceActive = Engine.Current.InputInterface.VR_Active;
            mouth.IsTracking = Engine.Current.InputInterface.VR_Active;

            mouth.JawOpen = expressions[(int)FBExpression2.Jaw_Drop] - expressions[(int)FBExpression2.Lips_Toward];

            float JawLR;

            if (InvertJaw)
            {
                JawLR = expressions[(int)FBExpression2.Jaw_Sideways_Left] - expressions[(int)FBExpression2.Jaw_Sideways_Right]; //Fixed inverted Jaw movement.
            }
            else
            {
                JawLR = expressions[(int)FBExpression2.Jaw_Sideways_Right] - expressions[(int)FBExpression2.Jaw_Sideways_Left]; //Fixed inverted Jaw movement.
            }

            mouth.Jaw = new float3(
                JawLR,
                expressions[(int)FBExpression2.Jaw_Drop],
                expressions[(int)FBExpression2.Jaw_Thrust]
            );

            mouth.LipUpperLeftRaise = expressions[(int)FBExpression2.Upper_Lip_Raiser_L];
            mouth.LipUpperRightRaise = expressions[(int)FBExpression2.Upper_Lip_Raiser_R];
            mouth.LipLowerLeftRaise = expressions[(int)FBExpression2.Lower_Lip_Depressor_L];
            mouth.LipLowerRightRaise = expressions[(int)FBExpression2.Lower_Lip_Depressor_R];

            var stretch = (expressions[(int)FBExpression2.Lip_Stretcher_L] + expressions[(int)FBExpression2.Lip_Stretcher_R]) / 2;
            mouth.LipUpperHorizontal = stretch;
            mouth.LipLowerHorizontal = stretch;

            mouth.MouthLeftSmileFrown = Math.Min(1, expressions[(int)FBExpression2.Lip_Corner_Puller_L] * 1.2f) - Math.Min(1, (expressions[(int)FBExpression2.Lip_Corner_Depressor_L] + expressions[(int)FBExpression2.Lip_Stretcher_L]) * SRANIPAL_NORMALIZER);//Math.Min(1, (expressions[(int)FBExpression2.Lip_Corner_Depressor_L]) * 1.5f);;
            mouth.MouthRightSmileFrown = Math.Min(1, expressions[(int)FBExpression2.Lip_Corner_Puller_R] * 1.2f) - Math.Min(1, (expressions[(int)FBExpression2.Lip_Corner_Depressor_R] + expressions[(int)FBExpression2.Lip_Stretcher_R]) * SRANIPAL_NORMALIZER);//Math.Min(1, (expressions[(int)FBExpression2.Lip_Corner_Depressor_R]) * 1.5f);;
            
            mouth.MouthPout = (expressions[(int)FBExpression2.Lip_Pucker_L] + expressions[(int)FBExpression2.Lip_Pucker_R]) / 3;

            // mouth.LipTopOverUnder = (expressions[(int)FBExpression2.Lip_Suck_LT] + expressions[(int)FBExpression2.Lip_Suck_RT]) / 2;
            // mouth.LipBottomOverturn = (expressions[(int)FBExpression2.Lip_Suck_LB] + expressions[(int)FBExpression2.Lip_Suck_RB]) / 2;

            mouth.LipTopOverturn = (expressions[(int)FBExpression2.Lips_Toward] + expressions[(int)FBExpression2.Lip_Funneler_LT] + expressions[(int)FBExpression2.Lip_Funneler_RT]) / 3;
            mouth.LipBottomOverturn = (expressions[(int)FBExpression2.Lips_Toward] + expressions[(int)FBExpression2.Lip_Funneler_LB] + expressions[(int)FBExpression2.Lip_Funneler_RB]) / 3;

            //if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft] > UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft];
            //else if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft] < UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft];

            //if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight] > UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight];
            //else if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight] < UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight];

            mouth.CheekLeftPuffSuck = expressions[(int)FBExpression2.Cheek_Puff_L] - expressions[(int)FBExpression2.Cheek_Suck_L];
            mouth.CheekRightPuffSuck = (expressions[(int)FBExpression2.Cheek_Puff_R]) - (expressions[(int)FBExpression2.Cheek_Suck_R]);
        }

        public float GetFaceExpression(int expressionIndex)
        {
            return expressions[expressionIndex];
        }

        public void Teardown()
        {
            try
            {
                cancellationTokenSource.Cancel();
                updateThread.Abort();
                cancellationTokenSource.Dispose();
                LibALXR.LibALXR.alxr_destroy();
            }
            catch (Exception ex)
            {
                ResoniteMod.Error("Exception when running teardown.");
                UniLog.Error(ex.ToString());
            }finally
            {
                _connected = false;
            }
        }

        public void JawState(bool input)
        {
            InvertJaw = input;
        }
        private static ALXRClientCtx CreateALXRClientCtx()
        {
            return new ALXRClientCtx
            {
                inputSend = (ref ALXRTrackingInfo data) => { },
                viewsConfigSend = (ref ALXREyeInfo eyeInfo) => { },
                pathStringToHash = (path) => { return (ulong)path.GetHashCode(); },
                timeSyncSend = (ref ALXRTimeSync data) => { },
                videoErrorReportSend = () => { },
                batterySend = (a, b, c) => { },
                setWaitingNextIDR = a => { },
                requestIDR = () => { },
                graphicsApi = config.GraphicsApi,
                decoderType = ALXRDecoderType.D311VA,
                displayColorSpace = ALXRColorSpace.Default,
                facialTracking = config.FacialTrackingExt,
                eyeTracking = config.EyeTrackingExt,
                trackingServerPortNo = LibALXR.LibALXR.TrackingServerDefaultPortNo,
                verbose = false,
                disableLinearizeSrgb = false,
                noSuggestedBindings = true,
                noServerFramerateLock = false,
                noFrameSkip = false,
                disableLocalDimming = true,
                headlessSession = false,
                simulateHeadless = false,
                noFTServer = true,
                noPassthrough = true,
                noHandTracking = !config.EnableHandleTracking,
                firmwareVersion = new ALXRVersion
                {
                    // only relevant for android clients.
                    major = 0,
                    minor = 0,
                    patch = 0
                }
            };
        }
    }
}
