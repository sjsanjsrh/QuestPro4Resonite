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
    public class ALXRModule : IQuestProModule
    {
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private bool connected = false;

        private bool InvertJaw;

        private const float SRANIPAL_NORMALIZER = 0.75f;
        private ALXRProcessFrameResult alxrResult;
        private float[] expressions = new float[(int)FBExpression2.Max];

        private double pitch_L, yaw_L, pitch_R, yaw_R; // Eye rotations

        private bool _connected = false;
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

        public bool Initialize(string dlldir)
        {
            if (!LibALXR.LibALXR.AddDllSearchPath(dlldir))
            {
                UniLog.Error($"[libalxr] unmanaged library path to search failed to be set.");
                return false;
            }
            config.DLLDir = dlldir;
            try
            {
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

                cancellationTokenSource = new CancellationTokenSource();
            }
            catch (Exception e)
            {
                UniLog.Error(e.Message);
                return false;
            }

            return true;
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
            }
            catch (Exception e)
            {
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
                    UniLog.Error("[libxr] failed connect to Quest Pro");
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
                        UniLog.Error(e.Message);
                        _connected = false;
                        Thread.Sleep(1000);
                    }
                }
            }
        }
    
        private void PrepareUpdate()
        {
            //// Eye Expressions

            //double q_x = expressions[(int)FBExpression.LeftRot_x];
            //double q_y = expressions[(int)FBExpression.LeftRot_y];
            //double q_z = expressions[(int)FBExpression.LeftRot_z];
            //double q_w = expressions[(int)FBExpression.LeftRot_w];

            //double yaw = Math.Atan2(2.0 * (q_y * q_z + q_w * q_x), q_w * q_w - q_x * q_x - q_y * q_y + q_z * q_z);
            //double pitch = Math.Asin(-2.0 * (q_x * q_z - q_w * q_y));
            //// Not needed for eye tracking
            //// double roll = Math.Atan2(2.0 * (q_x * q_y + q_w * q_z), q_w * q_w + q_x * q_x - q_y * q_y - q_z * q_z); 

            //// From radians
            //pitch_L = 180.0 / Math.PI * pitch; 
            //yaw_L = 180.0 / Math.PI * yaw;

            //q_x = expressions[(int)FBExpression.RightRot_x];
            //q_y = expressions[(int)FBExpression.RightRot_y];
            //q_z = expressions[(int)FBExpression.RightRot_z];
            //q_w = expressions[(int)FBExpression.RightRot_w];

            //yaw = Math.Atan2(2.0 * (q_y * q_z + q_w * q_x), q_w * q_w - q_x * q_x - q_y * q_y + q_z * q_z);
            //pitch = Math.Asin(-2.0 * (q_x * q_z - q_w * q_y));

            //// From radians
            //pitch_R = 180.0 / Math.PI * pitch; 
            //yaw_R = 180.0 / Math.PI * yaw;

            // Face Expressions

            // Eyelid edge case, eyes are actually closed now
            if (expressions[(int)FBExpression.Eyes_Look_Down_L] == expressions[(int)FBExpression.Eyes_Look_Up_L] && expressions[(int)FBExpression.Eyes_Closed_L] > 0.25f)
            { 
                expressions[(int)FBExpression.Eyes_Closed_L] = 0; // 0.9f - (expressions[(int)FBExpression.Lid_Tightener_L] * 3);
            }
            else
            {
                expressions[(int)FBExpression.Eyes_Closed_L] = 0.9f - ((expressions[(int)FBExpression.Eyes_Closed_L] * 3) / (1 + expressions[(int)FBExpression.Eyes_Look_Down_L] * 3));
            }

            // Another eyelid edge case
            if (expressions[(int)FBExpression.Eyes_Look_Down_R] == expressions[(int)FBExpression.Eyes_Look_Up_R] && expressions[(int)FBExpression.Eyes_Closed_R] > 0.25f)
            { 
                expressions[(int)FBExpression.Eyes_Closed_R] = 0; // 0.9f - (expressions[(int)FBExpression.Lid_Tightener_R] * 3);
            }
            else
            {
                expressions[(int)FBExpression.Eyes_Closed_R] = 0.9f - ((expressions[(int)FBExpression.Eyes_Closed_R] * 3) / (1 + expressions[(int)FBExpression.Eyes_Look_Down_R] * 3));
            }

            //expressions[(int)FBExpression.Lid_Tightener_L = 0.8f-expressions[(int)FBExpression.Eyes_Closed_L]; // Sad: fix combined param instead
            //expressions[(int)FBExpression.Lid_Tightener_R = 0.8f-expressions[(int)FBExpression.Eyes_Closed_R]; // Sad: fix combined param instead

            if (1 - expressions[(int)FBExpression.Eyes_Closed_L] < expressions[(int)FBExpression.Lid_Tightener_L])
                expressions[(int)FBExpression.Lid_Tightener_L] = (1 - expressions[(int)FBExpression.Eyes_Closed_L]) - 0.01f;

            if (1 - expressions[(int)FBExpression.Eyes_Closed_R] < expressions[(int)FBExpression.Lid_Tightener_R])
                expressions[(int)FBExpression.Lid_Tightener_R] = (1 - expressions[(int)FBExpression.Eyes_Closed_R]) - 0.01f;

            //expressions[(int)FBExpression.Lid_Tightener_L = Math.Max(0, expressions[(int)FBExpression.Lid_Tightener_L] - 0.15f);
            //expressions[(int)FBExpression.Lid_Tightener_R = Math.Max(0, expressions[(int)FBExpression.Lid_Tightener_R] - 0.15f);

            expressions[(int)FBExpression.Upper_Lid_Raiser_L] = Math.Max(0, expressions[(int)FBExpression.Upper_Lid_Raiser_L] - 0.5f);
            expressions[(int)FBExpression.Upper_Lid_Raiser_R] = Math.Max(0, expressions[(int)FBExpression.Upper_Lid_Raiser_R] - 0.5f);

            expressions[(int)FBExpression.Lid_Tightener_L] = Math.Max(0, expressions[(int)FBExpression.Lid_Tightener_L] - 0.5f);
            expressions[(int)FBExpression.Lid_Tightener_R] = Math.Max(0, expressions[(int)FBExpression.Lid_Tightener_R] - 0.5f);

            expressions[(int)FBExpression.Inner_Brow_Raiser_L] = Math.Min(1, expressions[(int)FBExpression.Inner_Brow_Raiser_L] * 3f); // * 4;
            expressions[(int)FBExpression.Brow_Lowerer_L] = Math.Min(1, expressions[(int)FBExpression.Brow_Lowerer_L] * 3f); // * 4;
            expressions[(int)FBExpression.Outer_Brow_Raiser_L] = Math.Min(1, expressions[(int)FBExpression.Outer_Brow_Raiser_L] * 3f); // * 4;

            expressions[(int)FBExpression.Inner_Brow_Raiser_R] = Math.Min(1, expressions[(int)FBExpression.Inner_Brow_Raiser_R] * 3f); // * 4;
            expressions[(int)FBExpression.Brow_Lowerer_R] = Math.Min(1, expressions[(int)FBExpression.Brow_Lowerer_R] * 3f); // * 4;
            expressions[(int)FBExpression.Outer_Brow_Raiser_R] = Math.Min(1, expressions[(int)FBExpression.Outer_Brow_Raiser_R] * 3f); // * 4;

            expressions[(int)FBExpression.Eyes_Look_Up_L] = expressions[(int)FBExpression.Eyes_Look_Up_L] * 0.55f;
            expressions[(int)FBExpression.Eyes_Look_Up_R] = expressions[(int)FBExpression.Eyes_Look_Up_R] * 0.55f;
            expressions[(int)FBExpression.Eyes_Look_Down_L] = expressions[(int)FBExpression.Eyes_Look_Down_L] * 1.5f;
            expressions[(int)FBExpression.Eyes_Look_Down_R] = expressions[(int)FBExpression.Eyes_Look_Down_R] * 1.5f;

            expressions[(int)FBExpression.Eyes_Look_Left_L] = expressions[(int)FBExpression.Eyes_Look_Left_L] * 0.85f;
            expressions[(int)FBExpression.Eyes_Look_Right_L] = expressions[(int)FBExpression.Eyes_Look_Right_L] * 0.85f;
            expressions[(int)FBExpression.Eyes_Look_Left_R] = expressions[(int)FBExpression.Eyes_Look_Left_R] * 0.85f;
            expressions[(int)FBExpression.Eyes_Look_Right_R] = expressions[(int)FBExpression.Eyes_Look_Right_R] * 0.85f;

            // Hack: turn rots to looks
            // Yitch = 29(left)-- > -29(right)
            // Yaw = -27(down)-- > 27(up)

            if (pitch_L > 0)
            {
                expressions[(int)FBExpression.Eyes_Look_Left_L] = Math.Min(1, (float)(pitch_L / 29.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression.Eyes_Look_Right_L] = 0;
            }
            else
            {
                expressions[(int)FBExpression.Eyes_Look_Left_L] = 0;
                expressions[(int)FBExpression.Eyes_Look_Right_L] = Math.Min(1, (float)((-pitch_L) / 29.0)) * SRANIPAL_NORMALIZER;
            }

            if (yaw_L > 0)
            {
                expressions[(int)FBExpression.Eyes_Look_Up_L] = Math.Min(1, (float)(yaw_L / 27.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression.Eyes_Look_Down_L] = 0;
            }
            else
            {
                expressions[(int)FBExpression.Eyes_Look_Up_L] = 0;
                expressions[(int)FBExpression.Eyes_Look_Down_L] = Math.Min(1, (float)((-yaw_L) / 27.0)) * SRANIPAL_NORMALIZER;
            }


            if (pitch_R > 0)
            {
                expressions[(int)FBExpression.Eyes_Look_Left_R] = Math.Min(1, (float)(pitch_R / 29.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression.Eyes_Look_Right_R] = 0;
            }
            else
            {
                expressions[(int)FBExpression.Eyes_Look_Left_R] = 0;
                expressions[(int)FBExpression.Eyes_Look_Right_R] = Math.Min(1, (float)((-pitch_R) / 29.0)) * SRANIPAL_NORMALIZER;
            }
            
            if (yaw_R > 0)
            {
                expressions[(int)FBExpression.Eyes_Look_Up_R] = Math.Min(1, (float)(yaw_R / 27.0)) * SRANIPAL_NORMALIZER;
                expressions[(int)FBExpression.Eyes_Look_Down_R] = 0;
            }
            else
            {
                expressions[(int)FBExpression.Eyes_Look_Up_R] = 0;
                expressions[(int)FBExpression.Eyes_Look_Down_R] = Math.Min(1, (float)((-yaw_R) / 27.0)) * SRANIPAL_NORMALIZER;
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

        public struct EyeGazeData
        {
            public bool isValid;
            public float3 position;
            public floatQ rotation;
            public float open;
            public float squeeze;
            public float wide;
            public float gazeConfidence;
        }

        public EyeGazeData GetEyeData(FBEye fbEye)
        {
            EyeGazeData eyeRet = new EyeGazeData();
            switch (fbEye)
            {
                case FBEye.Left:
                    eyeRet.position = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose0.position);
                    eyeRet.rotation = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose0.orientation);
                    eyeRet.open = MathX.Max(0, expressions[(int)FBExpression.Eyes_Closed_L]);
                    eyeRet.squeeze = expressions[(int)FBExpression.Lid_Tightener_L];
                    eyeRet.wide = expressions[(int)FBExpression.Upper_Lid_Raiser_L];
                    eyeRet.isValid = IsValid(eyeRet.position);
                    return eyeRet;
                case FBEye.Right:
                    eyeRet.position = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose1.position);
                    eyeRet.rotation = ALXRTypeToSystem(alxrResult.facialEyeTracking.eyeGazePose1.orientation);
                    eyeRet.open = MathX.Max(0, expressions[(int)FBExpression.Eyes_Closed_R]);
                    eyeRet.squeeze = expressions[(int)FBExpression.Lid_Tightener_R];
                    eyeRet.wide = expressions[(int)FBExpression.Upper_Lid_Raiser_R];
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

            mouth.JawOpen = expressions[(int)FBExpression.Jaw_Drop] - expressions[(int)FBExpression.Lips_Toward];

            float JawLR;

            if (InvertJaw)
            {
                JawLR = expressions[(int)FBExpression.Jaw_Sideways_Left] - expressions[(int)FBExpression.Jaw_Sideways_Right]; //Fixed inverted Jaw movement.
            }
            else
            {
                JawLR = expressions[(int)FBExpression.Jaw_Sideways_Right] - expressions[(int)FBExpression.Jaw_Sideways_Left]; //Fixed inverted Jaw movement.
            }

            mouth.Jaw = new float3(
                JawLR,
                expressions[(int)FBExpression.Jaw_Drop],
                expressions[(int)FBExpression.Jaw_Thrust]
            );

            mouth.LipUpperLeftRaise = expressions[(int)FBExpression.Upper_Lip_Raiser_L];
            mouth.LipUpperRightRaise = expressions[(int)FBExpression.Upper_Lip_Raiser_R];
            mouth.LipLowerLeftRaise = expressions[(int)FBExpression.Lower_Lip_Depressor_L];
            mouth.LipLowerRightRaise = expressions[(int)FBExpression.Lower_Lip_Depressor_R];

            var stretch = (expressions[(int)FBExpression.Lip_Stretcher_L] + expressions[(int)FBExpression.Lip_Stretcher_R]) / 2;
            mouth.LipUpperHorizontal = stretch;
            mouth.LipLowerHorizontal = stretch;

            mouth.MouthLeftSmileFrown = Math.Min(1, expressions[(int)FBExpression.Lip_Corner_Puller_L] * 1.2f) - Math.Min(1, (expressions[(int)FBExpression.Lip_Corner_Depressor_L] + expressions[(int)FBExpression.Lip_Stretcher_L]) * SRANIPAL_NORMALIZER);//Math.Min(1, (expressions[(int)FBExpression.Lip_Corner_Depressor_L]) * 1.5f);;
            mouth.MouthRightSmileFrown = Math.Min(1, expressions[(int)FBExpression.Lip_Corner_Puller_R] * 1.2f) - Math.Min(1, (expressions[(int)FBExpression.Lip_Corner_Depressor_R] + expressions[(int)FBExpression.Lip_Stretcher_R]) * SRANIPAL_NORMALIZER);//Math.Min(1, (expressions[(int)FBExpression.Lip_Corner_Depressor_R]) * 1.5f);;
            
            mouth.MouthPout = (expressions[(int)FBExpression.Lip_Pucker_L] + expressions[(int)FBExpression.Lip_Pucker_R]) / 3;

            // mouth.LipTopOverUnder = (expressions[(int)FBExpression.Lip_Suck_LT] + expressions[(int)FBExpression.Lip_Suck_RT]) / 2;
            // mouth.LipBottomOverturn = (expressions[(int)FBExpression.Lip_Suck_LB] + expressions[(int)FBExpression.Lip_Suck_RB]) / 2;

            mouth.LipTopOverturn = (expressions[(int)FBExpression.Lips_Toward] + expressions[(int)FBExpression.Lip_Funneler_LT] + expressions[(int)FBExpression.Lip_Funneler_RT]) / 3;
            mouth.LipBottomOverturn = (expressions[(int)FBExpression.Lips_Toward] + expressions[(int)FBExpression.Lip_Funneler_LB] + expressions[(int)FBExpression.Lip_Funneler_RB]) / 3;

            //if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft] > UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft];
            //else if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft] < UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileLeft] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadLeft];

            //if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight] > UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight];
            //else if (UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight] < UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight])
            //    UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSmileRight] /= 1 + UnifiedTrackingData.LatestLipData.LatestShapes[(int)UnifiedExpression.MouthSadRight];

            mouth.CheekLeftPuffSuck = expressions[(int)FBExpression.Cheek_Puff_L] - expressions[(int)FBExpression.Cheek_Suck_L];
            mouth.CheekRightPuffSuck = (expressions[(int)FBExpression.Cheek_Puff_R]) - (expressions[(int)FBExpression.Cheek_Suck_R]);
        }

        public float GetFaceExpression(int expressionIndex)
        {
            return expressions[expressionIndex];
        }

        public enum FBEye
        {
            Left,
            Right,
            Combined
        }

        public void Teardown()
        {
            try
            {
                cancellationTokenSource.Cancel();
                LibALXR.LibALXR.alxr_destroy();
                cancellationTokenSource.Dispose();
            }
            catch (Exception ex)
            {
                UniLog.Log("Exception when running teardown.");
                UniLog.Error(ex.ToString());
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
