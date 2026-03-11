using Elements.Core;
using FrooxEngine;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibALXR;
using ResoniteModLoader;

namespace QuestProModule.ALXR
{
    public class ALXRModuleLocal : ALXRModule, IQuestProModule
    {
        private Thread updateThread;

        public class Config
        {
            public ALXRGraphicsApi GraphicsApi { get; set; } = ALXRGraphicsApi.Auto;
            public ALXRFacialExpressionType FacialTrackingExt { get; set; } = ALXRFacialExpressionType.Auto;
            public ALXREyeTrackingType EyeTrackingExt { get; set; } = ALXREyeTrackingType.Auto;
            public bool EnableHandleTracking { get; set; } = true;
            public bool HeadlessSession { get; set; } = true;
            public bool SimulateHeadless { get; set; } = true;
            public bool VerboseLogs { get; set; } = true;
        }
        static Config config = new Config();

        new public Task<bool> Initialize(string _)
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(ALXRModuleLocal).Assembly, (libraryName, assembly, searchPath) =>
                {
                    if (NativeLibrary.TryLoad(libraryName, out IntPtr handle))
                        return handle;
                    return IntPtr.Zero;
                });

                cancellationTokenSource = new CancellationTokenSource();

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
                ResoniteMod.Msg($"connecting to Quest Pro");
                var ctx = CreateALXRClientCtx();
                var sysProperties = new ALXRSystemProperties();
                if (!LibALXR.LibALXR.alxr_init(ref ctx, out sysProperties))
                {
                    return false;
                }
                ResoniteMod.Msg($"Runtime Name: {sysProperties.systemName}");
                ResoniteMod.Msg($"Hand-tracking enabled? {sysProperties.IsHandTrackingEnabled}");
                ResoniteMod.Msg($"Eye-tracking enabled? {sysProperties.IsEyeTrackingEnabled}");
                ResoniteMod.Msg($"Face-tracking enabled? {sysProperties.IsHandTrackingEnabled}");
            }
            catch (Exception e)
            {
                ResoniteMod.Error("Exception when connecting to Quest Pro");
                UniLog.Error(e.Message);
                return false;
            }
            return true;
        }

        new public void Update()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (!ConnectALXR())
                {
                    ResoniteMod.Error("failed connect to Quest Pro");
                    Thread.Sleep(1000);
                    continue;
                }
                var alxrResult = new ALXRProcessFrameResult
                {
                    handTracking = new ALXRHandTracking(),
                    facialEyeTracking = new ALXRFacialEyePacket(),
                    exitRenderLoop = false,
                    requestRestart = false,
                };
                try
                {
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        alxrResult.exitRenderLoop = false;
                        LibALXR.LibALXR.alxr_process_frame2(ref alxrResult);
                        if (alxrResult.exitRenderLoop)
                        {
                            connected = false;
                            break;
                        }
                        connected = true;

                        packet = alxrResult.facialEyeTracking;

                        // Preprocess our expressions per Meta's Documentation
                        PrepareUpdate();

                        if (!LibALXR.LibALXR.alxr_is_session_running())
                        {
                            Thread.Sleep(250);
                            connected = false;
                        }
                    }
                    if (!alxrResult.requestRestart)
                    {
                        connected = false;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    ResoniteMod.Error("Exception when running update.");
                    UniLog.Error(ex.ToString());
                }
                finally
                {
                    try
                    {
                        LibALXR.LibALXR.alxr_destroy();
                    }
                    catch (Exception) { }
                }
            }
            ResoniteMod.Warn("update thread exited");
        }

        new public void Teardown()
        {
            try
            {
                cancellationTokenSource.Cancel();
                if(updateThread.Join(1000))
                {
                    updateThread.Abort();
                }
                cancellationTokenSource.Dispose();
                LibALXR.LibALXR.alxr_destroy();
            }
            catch (Exception ex)
            {
                ResoniteMod.Error("Exception when running teardown.");
                UniLog.Error(ex.ToString());
            }finally
            {
                connected = false;
            }
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
                faceTrackingDataSources = (uint)ALXRFaceTrackingDataSourceFlags.VisualSource,
                facialTracking = config.FacialTrackingExt,
                eyeTracking = config.EyeTrackingExt,
                trackingServerPortNo = LibALXR.LibALXR.TrackingServerDefaultPortNo,
                verbose = config.VerboseLogs,
                disableLinearizeSrgb = false,
                noSuggestedBindings = true,
                noServerFramerateLock = false,
                noFrameSkip = false,
                disableLocalDimming = true,
                headlessSession = config.HeadlessSession,
                simulateHeadless = config.SimulateHeadless,
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
