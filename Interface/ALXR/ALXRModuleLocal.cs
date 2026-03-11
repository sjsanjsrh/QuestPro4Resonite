using Elements.Core;
using FrooxEngine;
using System;
using System.IO;
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
        private ALXRLogOutputFn logOutputCallback;
        private IntPtr nativeCtxPtr = IntPtr.Zero;
        private ALXRClientCtx managedCtx;

        [DllImport(LibALXR.LibALXR.DllName, CallingConvention = LibALXR.LibALXR.ALXRCallingConvention, EntryPoint = "alxr_init")]
        private static extern bool alxr_init_native(IntPtr ctx, out ALXRSystemProperties systemProperties);

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
                var modDir = Path.GetDirectoryName(typeof(ALXRModuleLocal).Assembly.Location) ?? "";
                var libalxrDir = Directory.GetDirectories(modDir, "libalxr*");
                if (libalxrDir != null)
                { 
                    modDir = libalxrDir.Length > 0 ? libalxrDir[0] : modDir;
                }
                ResoniteMod.Msg($"libalxr directory: {modDir}");
                System.Runtime.InteropServices.DllImportResolver resolver = (libraryName, assembly, searchPath) =>
                {
                    IntPtr handle;
                    var fullPath = Path.Combine(modDir, libraryName);
                    if (NativeLibrary.TryLoad(fullPath, out handle))
                    {
                        ResoniteMod.Msg($"[libalxr] loaded '{libraryName}' from: {fullPath}");
                        return handle;
                    }
                    if (NativeLibrary.TryLoad(libraryName, out handle))
                    {
                        ResoniteMod.Warn($"[libalxr] loaded '{libraryName}' from system path (not mod dir)");
                        return handle;
                    }
                    ResoniteMod.Error($"[libalxr] failed to load '{libraryName}' (tried: {fullPath})");
                    return IntPtr.Zero;
                };
                NativeLibrary.SetDllImportResolver(typeof(LibALXR.LibALXR).Assembly, resolver);
                NativeLibrary.SetDllImportResolver(typeof(ALXRModuleLocal).Assembly, resolver);

                cancellationTokenSource = new CancellationTokenSource();

                ResoniteMod.Msg($"ALXRClientCtx marshal size: {Marshal.SizeOf<ALXRClientCtx>()} bytes");

                ResoniteMod.Msg($"[libalxr-dir] contents:");
                foreach (var f in Directory.GetFiles(modDir))
                    ResoniteMod.Msg($"  {Path.GetFileName(f)}");

                foreach (System.Diagnostics.ProcessModule mod in System.Diagnostics.Process.GetCurrentProcess().Modules)
                {
                    var name = mod.ModuleName.ToLowerInvariant();
                    if ((name.Contains("openxr") || name.Contains("alxr") || name.Contains("oxr"))
                        && !mod.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == false
                        && !System.Reflection.Assembly.GetExecutingAssembly().Location.Equals(mod.FileName, StringComparison.OrdinalIgnoreCase))
                        ResoniteMod.Warn($"[openxr-detect] already loaded: {mod.ModuleName} @ {mod.FileName}");
                }

                logOutputCallback = (level, output, len) =>
                {
                    var fullMsg = $"[libalxr] {output}";
                    switch (level)
                    {
                        case ALXRLogLevel.Verbose:
                            ResoniteMod.Debug(fullMsg);
                            break;
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
                };
                // alxr_set_log_custom_output is called in Update() on the update thread
                // so DLL load, alxr_set_log_custom_output, and alxr_init all happen on the same thread

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

        private void FreeNativeCtx()
        {
            if (nativeCtxPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(nativeCtxPtr);
                nativeCtxPtr = IntPtr.Zero;
            }
        }

        private bool ConnectALXR()
        {
            try
            {
                ResoniteMod.Msg("connecting to Quest Pro");
                FreeNativeCtx();
                managedCtx = CreateALXRClientCtx();
                nativeCtxPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ALXRClientCtx>());
                Marshal.StructureToPtr(managedCtx, nativeCtxPtr, false);
                var sysProperties = new ALXRSystemProperties();
                if (!alxr_init_native(nativeCtxPtr, out sysProperties))
                {
                    return false;
                }
                ResoniteMod.Msg($"Runtime Name: {sysProperties.systemName}");
                ResoniteMod.Msg($"Hand-tracking enabled? {sysProperties.IsHandTrackingEnabled}");
                ResoniteMod.Msg($"Eye-tracking enabled? {sysProperties.IsEyeTrackingEnabled}");
                ResoniteMod.Msg($"Face-tracking enabled? {sysProperties.IsFaceTrackingEnabled}");
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
            // All alxr_engine.dll calls on this thread: DLL load, set_log, alxr_init
            LibALXR.LibALXR.alxr_set_log_custom_output(ALXRLogOptions.None, logOutputCallback);
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
                FreeNativeCtx();
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

        private ALXRClientCtx CreateALXRClientCtx()
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
