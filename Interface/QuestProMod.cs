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
        public override string Name => "QuestPro4Resonite";
        public override string Author => "dfgHiatus & Geenz & Sinduy & Dante Tucker & ScarsTRF";
        public override string Version => "2.1.4";
        public override string Link => "https://github.com/sjsanjsrh/QuestPro4Resonite";

        [AutoRegisterConfigKey]
        private readonly static ModConfigurationKey<bool> EnableMode = new ModConfigurationKey<bool>("quest_pro_enabled", "Enable Quest Pro Mode", () => true);

		[AutoRegisterConfigKey]
        private readonly static ModConfigurationKey<string> QuestProIP = new ModConfigurationKey<string>("quest_pro_IP", "Quest Pro IP. This can be found in ALXR's settings, requires a restart to take effect", () => "127.0.0.1");

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

        //[AutoRegisterConfigKey]
        //private static readonly ModConfigurationKey<bool> LocalMode = new ModConfigurationKey<bool>("local_mode", "Mode to run without apk installation (do not check it as it does not work yet)", () => false);

        public static IQuestProModule qpm;

        public static EyeDevice edm;
        public static MouthDevice mdm;

        static ModConfiguration _config;

        public static float EyeOpenExponent = 1.0f;
        public static float EyeWideMult = 1.0f;
        public static float EyeMoveMulti = 1.0f;

		public override void OnEngineInit()
		{
            _config = GetConfiguration();

            _config.OnThisConfigurationChanged += OnConfigurationChanged;

            new Harmony("com.Sinduy.QuestPro4Resonite").PatchAll();
		}

        [HarmonyPatch(typeof(InputInterface), MethodType.Constructor)]
        [HarmonyPatch(new Type[] { typeof(Engine) })]
        public class InputInterfaceCtorPatch
        {
            public static void Postfix(InputInterface __instance)
            {
                try
                {
                    //if (_config.GetValue(LocalMode))
                    if (false)
                    {
                        qpm = new ALXRModuleLocal();
                    }
                    else
                    {
                        qpm = new ALXRModule();
                    }

                    if (!_config.TryGetValue(QuestProIP, out string ip)) 
                    { 
                        ip = "127.0.0.1"; 
                    }
                    if(_config.GetValue(EnableMode))
                    {
                        qpm.Initialize(ip);
                    }

                    qpm.JawState(_config.GetValue(InvertJaw));

                    edm = new EyeDevice();
                    mdm = new MouthDevice();

                    if (_config.TryGetValue(PupilSize, out float scale))
                    {
                        scale *= 0.01f;
                        edm.SetPupilSize(scale);
                    }

                    __instance.RegisterInputDriver(edm);
                    __instance.RegisterInputDriver(mdm);
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
            Msg($"Var changed! {@event.Label}");

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

            if (@event.Key == EnableMode)
            {
                if(_config.GetValue(EnableMode))
                {
                    if (@event.Config.TryGetValue(QuestProIP, out string ip))
                    {
                        qpm.Initialize(ip).Wait();
                    }
                }
                else
                {
                    qpm.Teardown();
                }
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
