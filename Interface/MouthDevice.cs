using Elements.Core;
using FrooxEngine;
using System.Collections.Generic;

namespace QuestProModule
{
    public class MouthDevice : IInputDriver
    {
        private Mouth mouth;
        public int UpdateOrder => 100;
        private InputInterface _input;

        public MouthDevice()
        {
            Engine.Current.OnShutdown += Teardown;
        }

        private void Teardown()
        {
            mouth.IsTracking = false;
        }

        public void CollectDeviceInfos(DataTreeList list)
        {
            var mouthDataTreeDictionary = new DataTreeDictionary();
            mouthDataTreeDictionary.Add("Name", "Quest Pro Face Tracking");
            mouthDataTreeDictionary.Add("Type", "Lip Tracking");
            mouthDataTreeDictionary.Add("Model", "Quest Pro");
            list.Add(mouthDataTreeDictionary);
        }

        public void RegisterInputs(InputInterface inputInterface)
        {
            mouth = new Mouth(inputInterface, "Quest Pro Mouth Tracking", new MouthParameterGroup[]
            {
                MouthParameterGroup.JawPose,
                MouthParameterGroup.JawOpen,
                MouthParameterGroup.TonguePose,
                MouthParameterGroup.LipRaise,
                MouthParameterGroup.LipHorizontal,
                MouthParameterGroup.SmileFrown,
                MouthParameterGroup.MouthPout,
                MouthParameterGroup.LipOverturn,
                MouthParameterGroup.CheekPuffSuck,
            });
            _input = inputInterface;
        }

        public void UpdateInputs(float deltaTime)
        {
            QuestProMod.qpm.GetFacialExpressions(mouth);
        }
    }
}
