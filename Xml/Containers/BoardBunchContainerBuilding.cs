﻿using UnityEngine;

namespace Klyte.WriteTheSigns.Xml
{

    public class BoardBunchContainerBuilding : IBoardBunchContainer
    {
        public uint m_linesUpdateFrame;
        public PropInfo m_cachedProp;

        public Vector3? m_cachedPosition;
        public Vector3? m_cachedRotation;
        public Vector3? m_cachedScale;
        public Matrix4x4 m_cachedMatrix;

        public int m_cachedArrayRepeatTimes;
        public Vector3 m_cachedOriginalPosition;
        public Vector3 m_cachedArrayItemPace;

        public bool HasAnyBoard() => true;

    }

}