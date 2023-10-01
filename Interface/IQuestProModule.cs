using System.Threading.Tasks;
using ResoniteModLoader;

namespace QuestProModule
{
    public interface IQuestProModule
    {
        public Task<bool> Initialize(string ipaddress);

        public void Update();

        public void Teardown();
    }
}
