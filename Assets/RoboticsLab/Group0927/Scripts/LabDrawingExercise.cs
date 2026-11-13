using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>只负责画笔留下的笔画和绘图进度，由工位统一调用。</summary>
    public sealed class LabDrawingExercise : IDisposable
    {
        readonly Group0927Station station;
        readonly List<LineRenderer> strokes = new List<LineRenderer>();
        readonly Material lineMaterial;
        LineRenderer currentStroke;
        Vector3 lastInk;
        float inkLength;

        public float Progress { get; private set; }
        public IReadOnlyList<LineRenderer> Strokes => strokes;

        public LabDrawingExercise(Group0927Station station)
        {
            this.station = station;
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        public void Clear()
        {
            foreach (var stroke in strokes)
                if (stroke) UnityEngine.Object.Destroy(stroke.gameObject);
            strokes.Clear();
            currentStroke = null;
            inkLength = 0;
            Progress = 0;
        }

        /// <summary>笔尖离纸、急停和离开工位时结束当前笔画。</summary>
        public void EndStroke()
        {
            currentStroke = null;
        }

        public void SetVisible(bool visible)
        {
            foreach (var stroke in strokes)
                if (stroke) stroke.gameObject.SetActive(visible);
        }

        public void Tick(bool canDraw)
        {
            if (!canDraw || !station.endEffector)
            {
                EndStroke();
                return;
            }

            Vector3 point = station.transform.InverseTransformPoint(station.ActualToolPosition);
            float gap = point.y - station.drawingHeight;
            if (gap < -.0002f || gap > .01f)
            {
                EndStroke();
                return;
            }

            point.y = station.drawingHeight + .003f * station.exerciseScale;
            if (currentStroke && Vector3.Distance(lastInk, point) < .004f * station.exerciseScale)
                return;

            if (!currentStroke)
            {
                StartStroke(point);
                return;
            }

            inkLength += Vector3.Distance(lastInk, point);
            lastInk = point;
            int count = currentStroke.positionCount;
            currentStroke.positionCount = count + 1;
            currentStroke.SetPosition(count, point);
            Progress = Mathf.Clamp01(inkLength / (2f * station.exerciseScale));
        }

        void StartStroke(Vector3 point)
        {
            var go = new GameObject("DrawingStroke");
            go.transform.SetParent(station.transform, false);
            currentStroke = go.AddComponent<LineRenderer>();
            currentStroke.sharedMaterial = lineMaterial;
            currentStroke.useWorldSpace = false;
            currentStroke.widthMultiplier = .007f * station.exerciseScale;
            currentStroke.startColor = currentStroke.endColor = new Color(.02f, .24f, .7f);
            currentStroke.numCapVertices = 4;
            currentStroke.positionCount = 1;
            currentStroke.SetPosition(0, point);
            strokes.Add(currentStroke);
            lastInk = point;
        }

        public void Dispose()
        {
            if (lineMaterial) UnityEngine.Object.Destroy(lineMaterial);
        }
    }
}
