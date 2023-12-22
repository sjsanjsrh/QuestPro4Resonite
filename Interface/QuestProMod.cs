using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using QuestProModule.ALXR;
using Elements.Core;
using System; 
using System.Threading;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;

namespace QuestProModule
{
    public class QuestProMod : ResoniteMod
    {
		[AutoRegisterConfigKey]
		private readonly static ModConfigurationKey<string> libalxr_path = new ModConfigurationKey<string>("libalxr_path", "path of libalxr", () => "libalxr-win-x64");

        [AutoRegisterConfigKey]
        private readonly static ModConfigurationKey<float> EyeOpennessExponent = new ModConfigurationKey<float>("quest_pro_eye_open_exponent", "Exponent to apply to eye openness.  Can be updated at runtime.  Useful for applying different curves for how open your eyes are.", () => 1.0f);

        [AutoRegisterConfigKey]
        private readonly static ModConfigurationKey<float> EyeWideMultiplier = new ModConfigurationKey<float>("quest_pro_eye_wide_multiplier", "Multiplier to apply to eye wideness.  Can be updated at runtime.  Useful for multiplying the amount your eyes can widen by.", () => 1.0f);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<float> EyeMovementMultiplier = new ModConfigurationKey<float>("quest_pro_eye_movement_multiplier", "Multiplier to adjust the movement range of the user's eyes.  Can be updated at runtime.", () => 1.0f);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<float> PupilSize = new ModConfigurationKey<float>("quest_pro_eye_pupil_size", "Used to adjust pupil size to a custom static size. (Best values are between 0.2 and 0.8, though might vary.", () => 0.5f);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> InvertJaw = new ModConfigurationKey<bool>("quest_pro_Invert_Jaw", "Value to invert Jaw Left/Right movement. (Only use if your jaw is inverted from your movements)", () => false);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> ResetEventInput = new ModConfigurationKey<bool>("quest_pro_reset_event", "Press to reinitialize the Quest Pro Module. (ONLY PRESS ONCE)", () => false);

        public static ALXRModule qpm;

        public static EyeDevice edm;

        static ModConfiguration _config;

        public static float EyeOpenExponent = 1.0f;
        public static float EyeWideMult = 1.0f;
        public static float EyeMoveMulti = 1.0f;

        public override string Name => "QuestPro4Resonite";
		public override string Author => "dfgHiatus & Geenz & Sinduy & Dante Tucker & ScarsTRF";
		public override string Version => "2.0.0";
		public override string Link => "https://github.com/sjsanjsrh/QuestPro4Resonite";
		public override void OnEngineInit()
		{
            _config = GetConfiguration();

            _config.OnThisConfigurationChanged += OnConfigurationChanged;

            new Harmony("net.dfgHiatus.QuestPro4Resonite").PatchAll();
		}

        [HarmonyPatch(typeof(InputInterface), MethodType.Constructor)]
        [HarmonyPatch(new Type[] { typeof(Engine) })]
        public class InputInterfaceCtorPatch
        {
            public static void Postfix(InputInterface __instance)
            {
                try
                {
                    qpm = new ALXRModule();
                    var path = _config.GetValue(libalxr_path);
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", path);
                        path = Path.GetFullPath(path);
                    }
                    Msg($"Loading libalxr from {path}");
                    qpm.Initialize(path);
                    qpm.JawState(_config.GetValue(InvertJaw));

                    edm = new EyeDevice();

                    if (_config.TryGetValue(PupilSize, out float scale))
                    {
                        scale = scale * 0.01f;
                        edm.SetPupilSize(scale);
                    }

                    __instance.RegisterInputDriver(edm);
                    __instance.RegisterInputDriver(new MouthDevice());
                }
                catch (Exception ex)
                {
                    Warn("Module failed to initiallize.");
                    Warn(ex.ToString());
                }
            }
        }

        private void OnConfigurationChanged(ConfigurationChangedEvent @event)
        {
            if (@event.Label == "NeosModSettings variable change") return;
            UniLog.Log($"Var changed! {@event.Label}");
            if (@event.Key == EyeOpennessExponent)
            {
                if (@event.Config.TryGetValue(EyeOpennessExponent, out float openExp))
                {
                    EyeOpenExponent = openExp;
                }
            }

            if (@event.Key == EyeOpennessExponent)
            {
                if (@event.Config.TryGetValue(EyeWideMultiplier, out float wideMult))
                {
                    EyeWideMult = wideMult;
                }
            }

            if (@event.Key == EyeMovementMultiplier)
            {
                if (@event.Config.TryGetValue(EyeMovementMultiplier, out float moveMulti))
                {
                    EyeMoveMulti = moveMulti;
                }
            }

            if (@event.Key == ResetEventInput)
            {
                qpm.Teardown();
                Thread.Sleep(1000);
                var path = _config.GetValue(libalxr_path);
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..", path);
                    path = Path.GetFullPath(path);
                }
                UniLog.Log($"Loading libalxr from {path}");
                qpm.Initialize(path);
            }

            if (@event.Key == InvertJaw)
            {
                if (@event.Config.TryGetValue(InvertJaw, out bool invertJaw))
                {
                    qpm.JawState(invertJaw);
                }
            }

            if (@event.Key == PupilSize)
            {
                if (@event.Config.TryGetValue(PupilSize, out float scale))
                {
                    scale = scale * 0.01f;
                    edm.SetPupilSize(scale);
                }
            }
        }
    }
}
