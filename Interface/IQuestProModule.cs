using System.Threading.Tasks;
using ResoniteModLoader;

namespace QuestProModule
{
    public interface IQuestProModule
    {
        public bool Initialize(string dlldir);

        public void Update();

        public void Teardown();
    }
}
