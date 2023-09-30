using BaseX;
using FrooxEngine;
using QuestProModule.ALXR;
using System;
using System.Numerics;
using static QuestProModule.ALXR.ALXRModule;

namespace QuestProModule
{
    public class EyeDevice : IInputDriver
    {
        private Eyes _eyes;
        InputInterface input;
        public int UpdateOrder => 100;

        public EyeDevice()
        {
            Engine.Current.OnShutdown += Teardown;
        }

        private void Teardown()
        {
            
        }

        public void CollectDeviceInfos(DataTreeList list)
        {
            var eyeDataTreeDictionary = new DataTreeDictionary();
            eyeDataTreeDictionary.Add("Name", "Quest Pro Eye Tracking");
            eyeDataTreeDictionary.Add("Type", "Eye Tracking");
            eyeDataTreeDictionary.Add("Model", "Quest Pro");
            list.Add(eyeDataTreeDictionary);
        }

        public void RegisterInputs(InputInterface inputInterface)
        {
            _eyes = new Eyes(inputInterface, "Quest Pro Eye Tracking");
            input = inputInterface;
        }

        public void UpdateInputs(float deltaTime)
        {
            if (!QuestProMod.qpm.Connected) 
            {
                _eyes.IsEyeTrackingActive = false;
                _eyes.LeftEye.IsTracking = false;
                _eyes.RightEye.IsTracking = false;
                _eyes.FinishUpdate();
                return;
            }
            _eyes.IsEyeTrackingActive = input.VR_Active;

            _eyes.LeftEye.IsTracking = input.VR_Active;
            _eyes.RightEye.IsTracking = input.VR_Active;

            var leftEyeData = QuestProMod.qpm.GetEyeData(FBEye.Left);
            var rightEyeData = QuestProMod.qpm.GetEyeData(FBEye.Right);

            _eyes.LeftEye.IsTracking &= leftEyeData.isValid;
            _eyes.LeftEye.RawPosition = leftEyeData.position;
            _eyes.LeftEye.UpdateWithRotation(leftEyeData.rotation);
            _eyes.LeftEye.PupilDiameter = 0.004f;
            _eyes.LeftEye.Widen = leftEyeData.wide;
            _eyes.LeftEye.Squeeze = leftEyeData.squeeze;
            _eyes.LeftEye.Openness = leftEyeData.open;

            _eyes.RightEye.IsTracking &= rightEyeData.isValid;
            _eyes.RightEye.RawPosition = rightEyeData.position;
            _eyes.RightEye.UpdateWithRotation(rightEyeData.rotation);
            _eyes.RightEye.PupilDiameter = 0.004f;
            _eyes.RightEye.Widen = rightEyeData.wide;
            _eyes.RightEye.Squeeze = rightEyeData.squeeze;
            _eyes.RightEye.Openness = rightEyeData.open;

            if (_eyes.LeftEye.IsTracking || _eyes.RightEye.IsTracking && (!_eyes.LeftEye.IsTracking || !_eyes.RightEye.IsTracking))
            {
                if (_eyes.LeftEye.IsTracking)
                {
                    _eyes.CombinedEye.RawPosition = _eyes.LeftEye.RawPosition;
                    _eyes.CombinedEye.UpdateWithRotation(_eyes.LeftEye.RawRotation);
                } else
                {
                    _eyes.CombinedEye.RawPosition = _eyes.RightEye.RawPosition;
                    _eyes.CombinedEye.UpdateWithRotation(_eyes.RightEye.RawRotation);
                }
                _eyes.CombinedEye.IsTracking = true;
            }
            else
            {
                _eyes.CombinedEye.IsTracking = false;
            }

            _eyes.CombinedEye.IsTracking = _eyes.LeftEye.IsTracking || _eyes.RightEye.IsTracking;
            _eyes.CombinedEye.RawPosition = (_eyes.LeftEye.RawPosition + _eyes.RightEye.RawPosition) * 0.5f;
            _eyes.CombinedEye.UpdateWithRotation(MathX.Slerp(_eyes.LeftEye.RawRotation, _eyes.RightEye.RawRotation, 0.5f));
            _eyes.CombinedEye.PupilDiameter = 0.004f;

            _eyes.LeftEye.Openness = MathX.Pow(_eyes.LeftEye.Openness, QuestProMod.EyeOpenExponent);
            _eyes.RightEye.Openness = MathX.Pow(_eyes.RightEye.Openness, QuestProMod.EyeOpenExponent);

            _eyes.ComputeCombinedEyeParameters();
            _eyes.ConvergenceDistance = 0f;
            _eyes.Timestamp += deltaTime;
            _eyes.FinishUpdate();
        }
    }
}
