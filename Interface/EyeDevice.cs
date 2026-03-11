using System;
using Elements.Core;
using FrooxEngine;
using LibALXR;

namespace QuestProModule
{
    public class EyeDevice : IInputDriver
    {
        private Eyes _eyes;
        InputInterface input;
        public int UpdateOrder => 100;
        private float pupilSize = 0.004f;

        public EyeDevice()
        {
            Engine.Current.OnShutdown += Teardown;
        }

        private void Teardown() { }

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
            _eyes = new Eyes(inputInterface, "Quest Pro Eye Tracking", true);
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

            var qpm = QuestProMod.qpm;

            _eyes.IsEyeTrackingActive = input.VR_Active;

            var leftEyeData = qpm.GetEyeData(FBEye.Left);
            var rightEyeData = qpm.GetEyeData(FBEye.Right);

            // Left eye
            _eyes.LeftEye.IsTracking = leftEyeData.isValid;
            _eyes.LeftEye.RawPosition = leftEyeData.position;
            _eyes.LeftEye.PupilDiameter = pupilSize;
            _eyes.LeftEye.Frown = qpm.GetFaceExpression((int)FBExpression2.Lip_Corner_Puller_L) - qpm.GetFaceExpression((int)FBExpression2.Lip_Corner_Depressor_L);
            _eyes.LeftEye.InnerBrowVertical = qpm.GetFaceExpression((int)FBExpression2.Inner_Brow_Raiser_L);
            _eyes.LeftEye.OuterBrowVertical = qpm.GetFaceExpression((int)FBExpression2.Outer_Brow_Raiser_L);
            _eyes.LeftEye.Squeeze = qpm.GetFaceExpression((int)FBExpression2.Brow_Lowerer_L);
            UpdateEye(_eyes.LeftEye, leftEyeData);

            // Right eye
            _eyes.RightEye.IsTracking = rightEyeData.isValid;
            _eyes.RightEye.RawPosition = rightEyeData.position;
            _eyes.RightEye.PupilDiameter = pupilSize;
            _eyes.RightEye.Frown = qpm.GetFaceExpression((int)FBExpression2.Lip_Corner_Puller_R) - qpm.GetFaceExpression((int)FBExpression2.Lip_Corner_Depressor_R);
            _eyes.RightEye.InnerBrowVertical = qpm.GetFaceExpression((int)FBExpression2.Inner_Brow_Raiser_R);
            _eyes.RightEye.OuterBrowVertical = qpm.GetFaceExpression((int)FBExpression2.Outer_Brow_Raiser_R);
            _eyes.RightEye.Squeeze = qpm.GetFaceExpression((int)FBExpression2.Brow_Lowerer_R);
            UpdateEye(_eyes.RightEye, rightEyeData);

            // Combined eye
            _eyes.CombinedEye.IsTracking = _eyes.LeftEye.IsTracking || _eyes.RightEye.IsTracking;
            _eyes.CombinedEye.RawPosition = (_eyes.LeftEye.RawPosition + _eyes.RightEye.RawPosition) * 0.5f;
            _eyes.CombinedEye.UpdateWithRotation(MathX.Slerp(_eyes.LeftEye.RawRotation, _eyes.RightEye.RawRotation, 0.5f));
            _eyes.CombinedEye.PupilDiameter = pupilSize;

            // Openness: 1 - clamp(EyesClosed + EyesClosed * LidTightener)
            float closedL = qpm.GetFaceExpression((int)FBExpression2.Eyes_Closed_L);
            float tightenerL = qpm.GetFaceExpression((int)FBExpression2.Lid_Tightener_L);
            _eyes.LeftEye.Openness = MathX.Pow(1.0f - Math.Max(0f, Math.Min(1f, closedL + closedL * tightenerL)), QuestProMod.EyeOpenExponent);

            float closedR = qpm.GetFaceExpression((int)FBExpression2.Eyes_Closed_R);
            float tightenerR = qpm.GetFaceExpression((int)FBExpression2.Lid_Tightener_R);
            _eyes.RightEye.Openness = MathX.Pow(1.0f - Math.Max(0f, Math.Min(1f, closedR + closedR * tightenerR)), QuestProMod.EyeOpenExponent);

            _eyes.ComputeCombinedEyeParameters();
            _eyes.ConvergenceDistance = 0f;
            _eyes.Timestamp += deltaTime;
            _eyes.FinishUpdate();
        }

        private void UpdateEye(Eye eye, EyeGazeData data)
        {
            bool valid = IsValid(data.open) && IsValid(data.position)
                      && IsValid(data.wide) && IsValid(data.squeeze)
                      && IsValid(data.rotation) && eye.IsTracking;
            eye.IsTracking = valid;

            if (eye.IsTracking)
            {
                eye.UpdateWithRotation(MathX.Slerp(floatQ.Identity, data.rotation, QuestProMod.EyeMoveMulti));
                eye.Openness = MathX.Pow(MathX.Max(0f, data.open), QuestProMod.EyeOpenExponent);
                eye.Widen = data.wide * QuestProMod.EyeWideMult;
            }
        }

        private static bool IsValid(float v) => !float.IsInfinity(v) && !float.IsNaN(v);
        private static bool IsValid(float3 v) => IsValid(v.x) && IsValid(v.y) && IsValid(v.z);
        private static bool IsValid(floatQ v) => IsValid(v.x) && IsValid(v.y) && IsValid(v.z) && IsValid(v.w);

        public void SetPupilSize(float input)
        {
            pupilSize = input;
        }
    }
}
