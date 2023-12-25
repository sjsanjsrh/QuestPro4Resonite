using System.Threading.Tasks;
using Elements.Core;
using ResoniteModLoader;

namespace QuestProModule
{
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
    public enum FBEye
    {
        Left,
        Right,
        Combined
    }
    public interface IQuestProModule
    {
        bool Connected { get; }

        public Task<bool> Initialize(string ipaddr);

        public void Update();

        public void Teardown();

        public void JawState(bool input);

        public EyeGazeData GetEyeData(FBEye fbEye);

        public void GetFacialExpressions(FrooxEngine.Mouth mouth);
    }
}
