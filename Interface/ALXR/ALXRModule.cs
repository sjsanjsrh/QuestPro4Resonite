using Elements.Core;
using FrooxEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LibALXR;
using System.Runtime.InteropServices;

namespace QuestProModule.ALXR
{
    public class ALXRModule : IQuestProModule
    {
        private IPAddress localAddr;
        private const int DEFAULT_PORT = 49192;
        
        private TcpClient client;
        private NetworkStream stream;
        private Thread tcpThread;
        protected CancellationTokenSource cancellationTokenSource;
        protected bool connected = false;

        private bool InvertJaw;

        protected const float SRANIPAL_NORMALIZER = 0.75f;
        private ALXRFacialEyePacket packet;
        private byte[] rawExpressions = new byte[Marshal.SizeOf<ALXRFacialEyePacket>()];
        protected float[] expressions = new float[(int)FBExpression2.Max];

        private double pitch_L, yaw_L, pitch_R, yaw_R; // Eye rotations

        public bool Connected
        { get { return connected; } }

        public async Task<bool> Initialize(string ipconfig)
        {
            try
            {
                localAddr = IPAddress.Parse(ipconfig);

                cancellationTokenSource = new CancellationTokenSource();

                UniLog.Log("Attempting to connect to TCP socket.");
                var connected = await ConnectToTCP(); // This should not block the main thread anymore...?
            } 
            catch (Exception e)
            {
                UniLog.Error(e.Message);
                return false;
            }

            if (connected) 
            {
                tcpThread = new Thread(Update);
                tcpThread.Start();
                return true;
            }

            return false;
        }

        private async Task<bool> ConnectToTCP()
        {
            try
            {
                client = new TcpClient();
                UniLog.Log($"Trying to establish a Quest Pro connection at {localAddr}:{DEFAULT_PORT}...");

                await client.ConnectAsync(localAddr, DEFAULT_PORT);

                if (client.Connected)
                {
                    UniLog.Log("Connected to Quest Pro!");

                    stream = client.GetStream();
                    connected = true;

                    return true;
                } 
                else
                {
                    UniLog.Error("Couldn't connect!");
                    return false;
                }
            }
            catch (Exception e)
            {
                UniLog.Error(e.Message);
                return false;
            }
        }

        public void Update()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    // Attempt reconnection if needed
                    if (!connected || stream == null)
                    {
                        ConnectToTCP().RunSynchronously();
                    }

                    // If the connection was unsuccessful, wait a bit and try again
                    if (stream == null)
                    {
                        UniLog.Warning("Didn't reconnect to the Quest Pro just yet! Trying again...");
                        return;
                    }

                    if (!stream.CanRead)
                    {
                        UniLog.Warning("Can't read from the Quest Pro network stream just yet! Trying again...");
                        return;
                    }

                    int offset = 0;
                    int readBytes;
                    do
                    {
                        readBytes = stream.Read(rawExpressions, offset, rawExpressions.Length - offset);
                        offset += readBytes;
                    }
                    while (readBytes > 0 && offset < rawExpressions.Length);

                    if (offset < rawExpressions.Length && connected)
                    {
                        UniLog.Warning("End of stream! Reconnecting...");
                        Thread.Sleep(1000);
                        connected = false;
                        try
                        {
                            stream.Close();
                        }
                        catch (SocketException e)
                        {
                            UniLog.Error(e.Message);
                            Thread.Sleep(1000);
                        }
                    }

                    packet = ALXRFacialEyePacket.ReadPacket(rawExpressions);
                    packet.ExpressionWeightSpan.CopyTo(expressions);

                    // Preprocess our expressions per Meta's Documentation
                    PrepareUpdate();
                }
                catch (SocketException e)
                {
                    UniLog.Error(e.Message);
                    Thread.Sleep(1000);
                }
            }         
        }
    
        protected void PrepareUpdate()
        {
            // Eye Expressions

            var q = packet.eyeGazePose0.orientation;

            // From radians
            pitch_L = 180.0 / Math.PI * q.Pitch;
            yaw_L = 180.0 / Math.PI * q.Yaw;

            q = packet.eyeGazePose1.orientation;

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
            return new floatQ(input.x, input.y, input.z, -input.w);
        }
        bool IsValid(float3 value) => IsValid(value.x) && IsValid(value.y) && IsValid(value.z);

        bool IsValid(float value) => !float.IsInfinity(value) && !float.IsNaN(value);

        public EyeGazeData GetEyeData(FBEye fbEye)
        {
            EyeGazeData eyeRet = new EyeGazeData();
            switch (fbEye)
            {
                case FBEye.Left:
                    eyeRet.position = ALXRTypeToSystem(packet.eyeGazePose0.position);
                    eyeRet.rotation = ALXRTypeToSystem(packet.eyeGazePose0.orientation);
                    eyeRet.open = MathX.Max(0, expressions[(int)FBExpression2.Eyes_Closed_L]);
                    eyeRet.squeeze = expressions[(int)FBExpression2.Lid_Tightener_L];
                    eyeRet.wide = expressions[(int)FBExpression2.Upper_Lid_Raiser_L];
                    eyeRet.isValid = IsValid(eyeRet.position);
                    return eyeRet;
                case FBEye.Right:
                    eyeRet.position = ALXRTypeToSystem(packet.eyeGazePose1.position);
                    eyeRet.rotation = ALXRTypeToSystem(packet.eyeGazePose1.orientation);
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
            mouth.CheekRightPuffSuck = expressions[(int)FBExpression2.Cheek_Puff_R] - expressions[(int)FBExpression2.Cheek_Suck_R];

            mouth.Tongue = new float3(0, 0, expressions[(int)FBExpression2.Tongue_Out] - expressions[(int)FBExpression2.Tongue_Retreat]);
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
                tcpThread.Abort();
                cancellationTokenSource.Dispose();
                stream.Close();
                stream.Dispose();
                client.Close();
                client.Dispose();
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
    }
}
