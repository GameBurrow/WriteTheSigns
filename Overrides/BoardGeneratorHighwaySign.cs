﻿using ColossalFramework;
using ColossalFramework.Globalization;
using ColossalFramework.Math;
using ColossalFramework.UI;
using Klyte.Commons.Extensors;
using Klyte.Commons.Overrides;
using Klyte.Commons.Utils;
using Klyte.DynamicTextBoards.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;
using ICities;
using static BuildingInfo;
using static Klyte.Commons.Utils.KlyteUtils;
using static Klyte.DynamicTextBoards.Overrides.BoardGeneratorHighwaySigns;
using System.IO;
using ColossalFramework.IO;
using System.Xml;

namespace Klyte.DynamicTextBoards.Overrides
{

    public class BoardGeneratorHighwaySigns : BoardGeneratorParent<BoardGeneratorHighwaySigns, BoardBunchContainerHighwaySign, CacheControlHighwaySign, BasicRenderInformation, BoardDescriptorHigwaySign, BoardTextDescriptor, ushort>, ISerializableDataExtension
    {


        public BasicRenderInformation[] m_cachedExitTitles;
        public BasicRenderInformation[] m_cachedDistanceMeshes;
        public List<ushort> m_destroyQueue = new List<ushort>();
        public List<string> LoadedProps { get; private set; }

        private UIDynamicFont m_font;


        public override int ObjArraySize => NetManager.MAX_SEGMENT_COUNT;
        public override UIDynamicFont DrawFont => m_font;

        #region Initialize
        public override void Initialize()
        {
            m_cachedExitTitles = new BasicRenderInformation[50];
            m_cachedDistanceMeshes = new BasicRenderInformation[100];
            LoadedProps = new List<string>();
            ForEachLoadedPrefab<PropInfo>((loaded) => LoadedProps.Add(loaded.name));
            LoadedProps.Sort((x, y) => Locale.Get("PROPS_TITLE", x).CompareTo(Locale.Get("PROPS_TITLE", y)));

            NetManagerOverrides.eventSegmentReleased += onSegmentReleased;

            BuildSurfaceFont(out m_font, "Highway Gothic");

            #region Hooks
            var afterRenderSegment = GetType().GetMethod("AfterRenderSegment", allFlags);
            AddRedirect(typeof(NetSegment).GetMethod("RenderInstance", new Type[] { typeof(RenderManager.CameraInfo), typeof(ushort), typeof(int) }), null, afterRenderSegment);
            #endregion
        }

        private void Start()
        {
            if (s_loadedBoards != null)
            {
                m_boardsContainers = s_loadedBoards;
                s_loadedBoards = null;
            }
        }


        protected override void OnTextureRebuilt()
        {
        }

        private void onSegmentReleased(ushort segmentId)
        {
            m_boardsContainers[segmentId] = null;
        }
        #endregion

        public static void AfterRenderSegment(RenderManager.CameraInfo cameraInfo, ushort segmentID, int layerMask)
        {

            instance.AfterRenderSegmentImpl(cameraInfo, segmentID, layerMask);

        }

        public void AfterRenderSegmentImpl(RenderManager.CameraInfo cameraInfo, ushort segmentID, int layerMask)
        {
            if (m_boardsContainers[segmentID] == null) return;
            if (!m_boardsContainers[segmentID].cached)
            {
                UpdateCache(segmentID);
                m_boardsContainers[segmentID].cached = true;
            }

            RenderSign(cameraInfo, segmentID, layerMask);

        }

        private void RenderSign(RenderManager.CameraInfo cameraInfo, ushort segmentID, int layerMask)
        {
            for (var i = 0; i < m_boardsContainers[segmentID]?.m_boardsData?.Length; i++)
            {
                var sign = m_boardsContainers[segmentID]?.m_boardsData[i];
                if (sign?.descriptor == null) continue;
                RenderPropMesh(ref sign.m_cachedProp, cameraInfo, segmentID, i, 0, layerMask, 0, sign.cachedPosition, Vector4.zero, ref sign.descriptor.m_propName, sign.cachedRotation, sign.descriptor.PropScale, ref sign.descriptor, out Matrix4x4 propMatrix, out bool rendered);
                if (rendered)
                {
                    for (int j = 0; j < sign.descriptor.m_textDescriptors?.Length; j++)
                    {
                        var block = NetManager.instance.m_materialBlock;
                        block.Clear();
                        RenderTextMesh(cameraInfo, segmentID, i, j, ref sign.descriptor, propMatrix, ref sign.descriptor.m_textDescriptors[j], ref sign, block);
                    }
                }

            }
        }

        private void UpdateCache(ushort segmentID)
        {

            for (var i = 0; i < m_boardsContainers[segmentID]?.m_boardsData?.Length; i++)
            {
                var sign = m_boardsContainers[segmentID]?.m_boardsData[i];
                if (sign.descriptor == null) continue;
                sign.m_cachedProp = null;
                var segmentInverted = (NetManager.instance.m_segments.m_buffer[segmentID].m_flags & NetSegment.Flags.Invert) > 0;
                var effectiveSegmentPos = segmentInverted ? 1 - sign.descriptor.m_segmentPosition : sign.descriptor.m_segmentPosition;
                var bezierPos = NetManager.instance.m_segments.m_buffer[segmentID].GetBezier().Position(effectiveSegmentPos);
                NetManager.instance.m_segments.m_buffer[segmentID].GetClosestPositionAndDirection(bezierPos, out Vector3 pos, out Vector3 dir);
                var rotation = dir.GetAngleXZ();
                if (sign.descriptor.m_invertSign != segmentInverted) rotation += 180;
                var rotationVectorX = VectorUtils.X_Y(DTBUtils.DegreeToVector2(rotation - 90));
                sign.cachedPosition = bezierPos + rotationVectorX * (NetManager.instance.m_segments.m_buffer[segmentID].Info.m_halfWidth + sign.descriptor.m_propPosition.x) + VectorUtils.X_Y(DTBUtils.DegreeToVector2(rotation)) * sign.descriptor.m_propPosition.z;
                sign.cachedPosition.y += sign.descriptor.m_propPosition.y;
                sign.cachedRotation = sign.descriptor.m_propRotation + new Vector3(0, rotation + 90);
            }
        }


        #region Serialize
        // Token: 0x17000063 RID: 99
        // (get) Token: 0x060001D0 RID: 464 RVA: 0x00002BD4 File Offset: 0x00000DD4
        public IManagers managers
        {
            get {
                return serializableDataManager?.managers;
            }
        }

        public ISerializableData serializableDataManager { get; private set; }

        public void OnCreated(ISerializableData serializableData)
        {
            serializableDataManager = serializableData;
        }

        public void OnReleased()
        {
        }

        // Token: 0x0600003A RID: 58 RVA: 0x00003F98 File Offset: 0x00002198
        public void OnLoadData()
        {
            if (Singleton<ToolManager>.instance.m_properties.m_mode != ItemClass.Availability.Game)
            {
                return;
            }
            m_boardsContainers = new BoardBunchContainerHighwaySign[NetManager.MAX_SEGMENT_COUNT];
            if (!serializableDataManager.EnumerateData().Contains(ID))
            {
                return;
            }
            using (MemoryStream memoryStream = new MemoryStream(serializableDataManager.LoadData(ID)))
            {
                byte[] storage = memoryStream.ToArray();
                Deserialize(System.Text.Encoding.UTF8.GetString(storage));
            }
        }

        // Token: 0x0600003B RID: 59 RVA: 0x00004020 File Offset: 0x00002220
        public void OnSaveData()
        {
            if (Singleton<ToolManager>.instance.m_properties.m_mode != ItemClass.Availability.Game)
            {
                return;
            }
            using (MemoryStream memoryStream = new MemoryStream())
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(Serialize());
                serializableDataManager.SaveData(ID, data);
            }
        }

        // Token: 0x04000019 RID: 25
        private const string ID = "K45_DTB_HS";

        // Token: 0x0400001A RID: 26
        private const int VERSION = 3;

        private static BoardBunchContainerHighwaySign[] s_loadedBoards;


        public static void Deserialize(string data)
        {
            DTBUtils.doLog($"STR: {data}");
            var parsedData = ParseSerialization(data.Split(SERIALIZATION_ITM_SEPARATOR.ToCharArray()));
            s_loadedBoards = new BoardBunchContainerHighwaySign[NetManager.MAX_SEGMENT_COUNT];
            foreach (var item in parsedData)
            {
                DTBUtils.doLog($"item: {item}");
                FillItem(item);
            }
        }

        private static void FillItem(IGrouping<ushort, Tuple<ushort, string>> item)
        {
            var count = item.Count();
            DTBUtils.doLog($"COUNT: {count}");
            s_loadedBoards[item.Key] = new BoardBunchContainerHighwaySign
            {
                m_boardsData = new CacheControlHighwaySign[count]
            };
            int i = 0;
            item.ForEach((x) =>
            {
                s_loadedBoards[item.Key].m_boardsData[i] = new CacheControlHighwaySign();
                s_loadedBoards[item.Key].m_boardsData[i].Deserialize(x.Second);
                i++;
            });
        }

        private static IEnumerable<IGrouping<ushort, Tuple<ushort, string>>> ParseSerialization(string[] data)
        {
            return data.Select(x =>
            {
                string[] dataArray = x.Split(SERIALIZATION_IDX_SEPARATOR.ToCharArray());
                return Tuple.New(ushort.Parse(dataArray[0]), dataArray[1]);

            }).GroupBy(x => x.First);
        }

        public static string Serialize()
        {
            var list = instance.m_boardsContainers?.SelectMany(SerializeSelectMany) ?? new List<string>();
            return string.Join(SERIALIZATION_ITM_SEPARATOR, list.ToArray());
        }
        private static IEnumerable<string> SerializeSelectMany(BoardBunchContainerHighwaySign x, int i)
        {
            return x?.m_boardsData?.Select(y => y == null ? null : $"{i}{SERIALIZATION_IDX_SEPARATOR}{ y.Serialize()}").Where(y => y != null) ?? new string[0];
        }


        public const string SERIALIZATION_IDX_SEPARATOR = "∂";
        public const string SERIALIZATION_ITM_SEPARATOR = "∫";
        #endregion


        #region Upadate Data
        protected override BasicRenderInformation GetMeshCurrentNumber(ushort id, int boardIdx, int kilometers)
        {
            if (m_cachedExitTitles.Length <= kilometers + 1)
            {
                m_cachedExitTitles = new BasicRenderInformation[kilometers + 1];
            }
            if (m_cachedExitTitles[kilometers] == null || lastFontUpdateFrame > m_cachedExitTitles[kilometers].m_frameDrawTime)
            {
                doLog($"!nameUpdated Node1 {kilometers}");
                RefreshNameData(ref m_cachedExitTitles[kilometers], $"Saída {kilometers}");
            }
            return m_cachedExitTitles[kilometers];

        }
        #endregion

        public override Color GetColor(ushort buildingID, int idx, int secIdx, BoardDescriptorHigwaySign descriptor)
        {
            return Color.white;

        }

        protected override InstanceID GetPropRenderID(ushort nodeId)
        {
            InstanceID result = default(InstanceID);
            result.NetNode = nodeId;
            return result;
        }

        public override Color GetContrastColor(ushort refID, int boardIdx, int secIdx, BoardDescriptorHigwaySign descriptor)
        {
            return Color.black;
        }



        public class CacheControlHighwaySign : CacheControl
        {
            public BoardDescriptorHigwaySign descriptor;
            public Vector3 cachedPosition;
            public Vector3 cachedRotation;

            public string Serialize()
            {
                XmlSerializer xmlser = new XmlSerializer(typeof(BoardDescriptorHigwaySign));
                XmlWriterSettings settings = new XmlWriterSettings { Indent = false };
                using (StringWriter textWriter = new StringWriter())
                {
                    using (XmlWriter xw = XmlWriter.Create(textWriter, settings))
                    {
                        XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                        ns.Add("", "");
                        xmlser.Serialize(xw, descriptor, ns);
                        return textWriter.ToString();
                    }
                }
            }

            public void Deserialize(String s)
            {
                XmlSerializer xmlser = new XmlSerializer(typeof(BoardDescriptorHigwaySign));
                try
                {
                    using (TextReader tr = new StringReader(s))
                    {
                        using (XmlReader reader = XmlReader.Create(tr))
                        {
                            if (xmlser.CanDeserialize(reader))
                            {
                                descriptor = (BoardDescriptorHigwaySign)xmlser.Deserialize(reader);
                            }
                            else
                            {
                                DTBUtils.doErrorLog($"CAN'T DESERIALIZE BOARD DESCRIPTOR!\nText : {s}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DTBUtils.doErrorLog($"CAN'T DESERIALIZE BOARD DESCRIPTOR!\nText : {s}\n{e.Message}\n{e.StackTrace}");
                }

            }
        }

        public class BoardDescriptorHigwaySign : BoardDescriptor
        {
            [XmlAttribute("inverted")]
            public bool m_invertSign = false;
            [XmlAttribute("segmentPosition")]
            public float m_segmentPosition = 0.5f;
        }

        public class BoardBunchContainerHighwaySign : IBoardBunchContainer<CacheControlHighwaySign, BasicRenderInformation>
        {
            public bool cached = false;
        }

    }
}
