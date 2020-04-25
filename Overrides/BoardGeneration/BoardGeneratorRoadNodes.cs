﻿using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using Klyte.Commons.Extensors;
using Klyte.Commons.Utils;
using Klyte.WriteTheSigns.Data;
using Klyte.WriteTheSigns.Rendering;
using Klyte.WriteTheSigns.Utils;
using Klyte.WriteTheSigns.Xml;
using SpriteFontPlus;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Klyte.WriteTheSigns.Overrides
{

    public class BoardGeneratorRoadNodes : Redirector, IRedirectable
    {
        public static BoardGeneratorRoadNodes Instance;
        public DynamicSpriteFont DrawFont => FontServer.instance[Data.DefaultFont] ?? FontServer.instance[WTSController.DEFAULT_FONT_KEY];
        public WTSRoadNodesData Data => WTSRoadNodesData.Instance;
        public bool[] m_updatedStreetPositions;
        public bool[] m_updatedDestinations;
        public uint[] m_lastFrameUpdate;

        private readonly Func<RenderManager, uint> m_getCurrentFrame = ReflectionUtils.GetGetFieldDelegate<RenderManager, uint>("m_currentFrame", typeof(RenderManager));

        private Dictionary<ItemClass, List<NetInfo>> m_allClasses;

        public Dictionary<ItemClass, List<NetInfo>> AllClasses
        {
            get {
                if (m_allClasses == null)
                {
                    m_allClasses = ((FastList<PrefabCollection<NetInfo>.PrefabData>)typeof(PrefabCollection<NetInfo>).GetField("m_scenePrefabs", RedirectorUtils.allFlags).GetValue(null)).m_buffer
                        .Select(x => x.m_prefab)
                        .Where(x => x?.m_class != null && x.m_class.m_service == ItemClass.Service.Road)
                        .GroupBy(x => x.m_class.name)
                        .ToDictionary(x => x.First().m_class, x => x.ToList());
                }
                return m_allClasses;
            }
        }

        #region Initialize
        public void Awake()
        {
            Instance = this;

            m_lastFrameUpdate = new uint[NetManager.MAX_NODE_COUNT];
            m_updatedStreetPositions = new bool[Data.ObjArraySize];
            m_updatedDestinations = new bool[Data.ObjArraySize];


            var adrEventsType = Type.GetType("Klyte.Addresses.ModShared.AdrEvents, KlyteAddresses");
            if (adrEventsType != null)
            {
                static void RegisterEvent(string eventName, Type adrEventsType, Action action) => adrEventsType.GetEvent(eventName)?.AddEventHandler(null, action);
                RegisterEvent("EventZeroMarkerBuildingChange", adrEventsType, new Action(OnZeroMarkChanged));
                RegisterEvent("EventRoadNamingChange", adrEventsType, new Action(OnZeroMarkChanged));
                RegisterEvent("EventDistrictColorChanged", adrEventsType, new Action(OnDistrictChanged));
                RegisterEvent("EventBuildingNameStrategyChanged", adrEventsType, new Action(OnZeroMarkChanged));
            }

            #region Hooks
            System.Reflection.MethodInfo postRenderMeshs = GetType().GetMethod("AfterRenderSegment", RedirectorUtils.allFlags);
            System.Reflection.MethodInfo orig = typeof(NetSegment).GetMethod("RenderInstance", new Type[] { typeof(RenderManager.CameraInfo), typeof(ushort), typeof(int) });
            LogUtils.DoLog($"Patching: {orig} => {postRenderMeshs} {postRenderMeshs.IsStatic}");
            AddRedirect(orig, null, postRenderMeshs);
            #endregion
        }

        public void Start()
        {
            NetManagerOverrides.EventNodeChanged += OnNodeChanged;
            WTSController.EventOnDistrictChanged += OnDistrictChanged;
            NetManagerOverrides.EventSegmentNameChanged += OnNameSeedChanged;
        }

        private void OnNodeChanged(ushort nodeId)
        {
            m_updatedStreetPositions[nodeId] = false;
            InvalidateNodeDestinationPaths(nodeId);
        }

        private void OnNameSeedChanged(ushort segmentId)
        {
            LogUtils.DoLog("onNameSeedChanged");
            m_updatedStreetPositions[NetManager.instance.m_segments.m_buffer[segmentId].m_endNode] = false;
            m_updatedStreetPositions[NetManager.instance.m_segments.m_buffer[segmentId].m_startNode] = false;
            InvalidateNodeDestinationPaths(NetManager.instance.m_segments.m_buffer[segmentId].m_endNode);
            InvalidateNodeDestinationPaths(NetManager.instance.m_segments.m_buffer[segmentId].m_startNode);
        }

        private void OnDistrictChanged()
        {
            LogUtils.DoLog("onDistrictChanged");
            m_updatedStreetPositions = new bool[Data.ObjArraySize];
            m_updatedDestinations = new bool[Data.ObjArraySize];
            WriteTheSignsMod.Controller?.StopAllCoroutines();
        }
        private void OnZeroMarkChanged()
        {
            LogUtils.DoLog("onZeroMarkChanged");
            m_updatedStreetPositions = new bool[Data.ObjArraySize];
            m_updatedDestinations = new bool[Data.ObjArraySize];
            WriteTheSignsMod.Controller?.StopAllCoroutines();
        }
        #endregion

        public static void AfterRenderSegment(RenderManager.CameraInfo cameraInfo, ushort segmentID)
        {
            Instance.AfterRenderInstanceImpl(cameraInfo, NetManager.instance.m_segments.m_buffer[segmentID].m_startNode, ref NetManager.instance.m_nodes.m_buffer[NetManager.instance.m_segments.m_buffer[segmentID].m_startNode]);
            Instance.AfterRenderInstanceImpl(cameraInfo, NetManager.instance.m_segments.m_buffer[segmentID].m_endNode, ref NetManager.instance.m_nodes.m_buffer[NetManager.instance.m_segments.m_buffer[segmentID].m_endNode]);
        }


        public void AfterRenderInstanceImpl(RenderManager.CameraInfo cameraInfo, ushort nodeID, ref NetNode data)
        {
            if (Data.DescriptorRulesOrder == null)
            {
                Data.DescriptorRulesOrder = new BoardInstanceRoadNodeXml[0];
            }
            if (Data.DescriptorRulesOrder.Length == 0 || m_lastFrameUpdate[nodeID] == m_getCurrentFrame(RenderManager.instance))
            {
                return;
            }

            m_lastFrameUpdate[nodeID] = m_getCurrentFrame(RenderManager.instance);
            if (data.CountSegments() < 2)
            {
                return;
            }

            if (!m_updatedStreetPositions[nodeID] || Data.BoardsContainers[nodeID, 0, 0] == null)
            {
                Data.BoardsContainers[nodeID, 0, 0] = new CacheRoadNodeItem();
                m_updatedStreetPositions[nodeID] = true;
                WriteTheSignsMod.Controller.StartCoroutine(CalculateSigns(nodeID));
            }

            for (int y = 0; y < Data.BoardsContainers.GetLength(1); y++)
            {
                for (int z = 0; z < Data.BoardsContainers.GetLength(2); z++)
                {
                    ref CacheRoadNodeItem item = ref Data.BoardsContainers[nodeID, y, z];
                    if (item?.m_renderPlate ?? false)
                    {
                        ref BoardInstanceRoadNodeXml targetDescriptor = ref item.m_currentDescriptor;
                        if (targetDescriptor?.Descriptor?.m_propName == null)
                        {
                            continue;
                        }

                        if (item.m_cachedProp?.name != targetDescriptor?.Descriptor?.m_propName)
                        {
                            item.m_cachedProp = null;
                        }
                        RenderSign(cameraInfo, nodeID, y, z, item.m_platePosition, item.m_streetDirection, ref targetDescriptor, ref item.m_cachedProp);
                    }
                }
            }
        }

        private void RenderSign(RenderManager.CameraInfo cameraInfo, ushort nodeID, int boardIdx, int secIdx, Vector3 position, float direction, ref BoardInstanceRoadNodeXml targetDescriptor, ref PropInfo cachedProp)
        {
            WTSPropRenderingRules.RenderPropMesh(ref cachedProp, cameraInfo, nodeID, boardIdx, secIdx, 0xFFFFFFF, 0, position, Vector4.zero, ref targetDescriptor.Descriptor.m_propName, new Vector3(0, direction) + targetDescriptor.m_propRotation, targetDescriptor.PropScale, targetDescriptor, out Matrix4x4 propMatrix, out bool rendered, GetPropRenderID(nodeID));
            if (rendered)
            {

                for (int j = 0; j < targetDescriptor.Descriptor.m_textDescriptors.Length; j++)
                {
                    if (cameraInfo.CheckRenderDistance(position, 200 * targetDescriptor.Descriptor.m_textDescriptors[j].m_textScale))
                    {
                        MaterialPropertyBlock properties = PropManager.instance.m_materialBlock;
                        properties.Clear();
                        WTSPropRenderingRules.RenderTextMesh(nodeID, boardIdx, secIdx, targetDescriptor, propMatrix, targetDescriptor.Descriptor.m_textDescriptors[j], properties, DrawFont);
                    }
                }
            }
        }
        #region calculation
        private IEnumerator CalculateSigns(ushort nodeID)
        {
            yield return 0;
            var incomingTraffic = new HashSet<ushort>();
            var outcomingTraffic = new HashSet<ushort>();
            for (int i = 0; i < 8; i++)
            {
                ushort segmentIid = NetManager.instance.m_nodes.m_buffer[nodeID].GetSegment(i);
                if (segmentIid != 0)
                {
                    bool invertFlagI = (Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].m_flags & NetSegment.Flags.Invert) != 0;
                    bool isSegmentIinverted = invertFlagI == (Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].m_startNode == nodeID) != (SimulationManager.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.False);
                    if ((isSegmentIinverted && Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info.m_hasBackwardVehicleLanes) || (!isSegmentIinverted && Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info.m_hasForwardVehicleLanes))
                    {
                        incomingTraffic.Add(segmentIid);
                    }
                    if ((!isSegmentIinverted && Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info.m_hasBackwardVehicleLanes) || (isSegmentIinverted && Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info.m_hasForwardVehicleLanes))
                    {
                        outcomingTraffic.Add(segmentIid);
                    }
                }
            }

            yield return 0;
            LogUtils.DoLog($"updatedStreets! {nodeID} {NetManager.instance.m_nodes.m_buffer[nodeID].CountSegments()}");
            int controlBoardIdx = 0;
            for (int i = 0; i < 8; i++)
            {
                ushort segmentIid = NetManager.instance.m_nodes.m_buffer[nodeID].GetSegment(i);
                if (segmentIid != 0)
                {
                    if (Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info != null && Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info.m_netAI is RoadBaseAI roadAiI)
                    {
                        GetNeigborSegment(nodeID, ref NetManager.instance.m_nodes.m_buffer[nodeID], segmentIid, ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid], out Vector3 segmentIDirection, out Vector3 segmentJDirection, out ushort segmentJid, out bool invertIJ);

                        ItemClass classI = Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid].Info.m_class;
                        ItemClass classJ = Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid].Info.m_class;
                        var matchingDescriptors = new Stack<Tuple<int, BoardInstanceRoadNodeXml>>(Data.DescriptorRulesOrder.Select((x, y) => Tuple.New(y, x)).Where(x => x.Second.AllowsClass(classI) || x.Second.AllowsClass(classJ)).OrderByDescending(x => x.First));
                        if (matchingDescriptors.Count == 0)
                        {
                            continue;
                        }
                        int secondaryIdx = 0;
                        bool hasSpawned = false;
                        BoardInstanceRoadNodeXml targetDescriptor = null;
                        while (matchingDescriptors.Count > 0 && !hasSpawned)
                        {
                            targetDescriptor = matchingDescriptors.Pop()?.Second;
                            hasSpawned = ProcessDescriptor(nodeID, ref NetManager.instance.m_nodes.m_buffer[nodeID], controlBoardIdx, segmentIid, ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid], segmentIDirection, segmentJid, ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid], segmentJDirection, invertIJ, targetDescriptor, incomingTraffic, outcomingTraffic, ref secondaryIdx);

                            yield return 0;
                        }
                        if (matchingDescriptors.Count > 0 && targetDescriptor.PlaceOnSegmentInsteadOfCorner && targetDescriptor.AllowAnotherRuleForCorner)
                        {
                            targetDescriptor = matchingDescriptors.Where((x) => !x.Second.PlaceOnSegmentInsteadOfCorner).FirstOrDefault()?.Second;
                            if (targetDescriptor != null)
                            {
                                hasSpawned |= ProcessDescriptor(nodeID, ref NetManager.instance.m_nodes.m_buffer[nodeID], controlBoardIdx, segmentIid, ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentIid], segmentIDirection, segmentJid, ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid], segmentJDirection, invertIJ, targetDescriptor, incomingTraffic, outcomingTraffic, ref secondaryIdx);
                            }
                        }
                        if (hasSpawned)
                        {

                            lock (Data.BoardsContainers)
                            {
                                while (secondaryIdx < 4)
                                {
                                    Data.BoardsContainers[nodeID, controlBoardIdx, secondaryIdx++] = null;
                                }
                            };
                            controlBoardIdx++;
                        }

                        yield return 0;
                    }
                }
            }

            lock (Data.BoardsContainers)
            {
                if (controlBoardIdx == 0)
                {
                    Data.BoardsContainers[nodeID, 0, 0] = new CacheRoadNodeItem();
                    Data.BoardsContainers[nodeID, 0, 1] = null;
                    Data.BoardsContainers[nodeID, 0, 2] = null;
                    Data.BoardsContainers[nodeID, 0, 3] = null;
                    controlBoardIdx++;
                }
                while (controlBoardIdx < 8)
                {

                    Data.BoardsContainers[nodeID, controlBoardIdx, 0] = null;
                    Data.BoardsContainers[nodeID, controlBoardIdx, 1] = null;
                    Data.BoardsContainers[nodeID, controlBoardIdx, 2] = null;
                    Data.BoardsContainers[nodeID, controlBoardIdx, 3] = null;
                    controlBoardIdx++;
                }
            }

            m_updatedStreetPositions[nodeID] = true;
        }

        private bool ProcessDescriptor(
            ushort nodeID, ref NetNode data, int controlBoardIdx,
            ushort segmentIid, ref NetSegment netSegmentI, Vector3 segmentIDirection,
            ushort segmentJid, ref NetSegment netSegmentJ, Vector3 segmentJDirection, bool invertIJ,
            BoardInstanceRoadNodeXml targetDescriptor, HashSet<ushort> incoming,
            HashSet<ushort> outcoming, ref int subboardOffset)
        {
            if (targetDescriptor?.Descriptor?.m_propName == null)
            {
                return false;
            }
            bool invertFlagI = (netSegmentI.m_flags & NetSegment.Flags.Invert) != 0;
            bool invertFlagJ = (netSegmentJ.m_flags & NetSegment.Flags.Invert) != 0;
            bool isSegmentIinverted = invertFlagI == (netSegmentI.m_startNode == nodeID) != (SimulationManager.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.False);
            bool isSegmentJinverted = invertFlagJ == (netSegmentJ.m_startNode == nodeID) != (SimulationManager.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.False);

            if (segmentJid == 0
                || !(Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid].Info.m_netAI is RoadBaseAI)
                || (targetDescriptor.IgnoreEmptyNameRoads && (Singleton<NetManager>.instance.GetSegmentName(segmentIid).IsNullOrWhiteSpace() || Singleton<NetManager>.instance.GetSegmentName(segmentJid).IsNullOrWhiteSpace()))
                || SegmentUtils.IsSameName(segmentJid, segmentIid, false, false, true, targetDescriptor.PlaceOnDistrictBorder, targetDescriptor.PlaceOnTunnelBridgeStart)
                || (new Randomizer(segmentIid | (segmentJid << 16)).UInt32(255) > targetDescriptor.SpawnChance))
            {
                return false;
            }
            Vector3 platePosI;
            Vector3 platePosJ;
            bool renderJ = true;
            bool renderI = true;

            /*
             
                || Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid].Info?.m_halfWidth > targetDescriptor.MaxRoadHalfWidth + 0.002f
                || Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid].Info?.m_halfWidth < targetDescriptor.MinRoadHalfWidth - 0.002f
             */
            if (targetDescriptor.PlaceOnSegmentInsteadOfCorner)
            {
                renderJ = !targetDescriptor.EnsureSegmentTypeInAllowedTypes || targetDescriptor.AllowsClass(netSegmentJ.Info.m_class);
                renderI = !targetDescriptor.EnsureSegmentTypeInAllowedTypes || targetDescriptor.AllowsClass(netSegmentI.Info.m_class);
                platePosI = CaculateCenterSegment(nodeID, ref netSegmentI, targetDescriptor, out segmentIDirection.y, invertIJ);
                platePosJ = CaculateCenterSegment(nodeID, ref netSegmentJ, targetDescriptor, out segmentJDirection.y, !invertIJ);
                if (targetDescriptor.TrafficDirectionRequired == TrafficDirectionRequired.OUTCOMING)
                {
                    renderI &= !invertIJ
                        && (
                             (!isSegmentIinverted && netSegmentI.Info.m_backwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentI.Info.m_backwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes)
                           || (isSegmentIinverted && netSegmentI.Info.m_forwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentI.Info.m_forwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes)
                           ) && netSegmentI.Info?.m_halfWidth < targetDescriptor.MaxRoadHalfWidth + 0.002f && netSegmentI.Info?.m_halfWidth > targetDescriptor.MinRoadHalfWidth - 0.002f;
                    renderJ &= invertIJ && ((!isSegmentJinverted && netSegmentJ.Info.m_backwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentJ.Info.m_backwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes)
                                          || (isSegmentJinverted && netSegmentJ.Info.m_forwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentJ.Info.m_forwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes))
                                          && netSegmentJ.Info?.m_halfWidth < targetDescriptor.MaxRoadHalfWidth + 0.002f && netSegmentJ.Info?.m_halfWidth > targetDescriptor.MinRoadHalfWidth - 0.002f;

                    if (incoming.Count == 1)
                    {
                        renderI &= !SegmentUtils.IsSameName(incoming.First(), segmentIid, false, false, true, targetDescriptor.PlaceOnDistrictBorder, targetDescriptor.PlaceOnTunnelBridgeStart);
                        renderJ &= !SegmentUtils.IsSameName(incoming.First(), segmentJid, false, false, true, targetDescriptor.PlaceOnDistrictBorder, targetDescriptor.PlaceOnTunnelBridgeStart);
                    }
                }
                else if (targetDescriptor.TrafficDirectionRequired == TrafficDirectionRequired.INCOMING)
                {
                    if (outcoming.Count < targetDescriptor.MinNodeOutcomingLanes || outcoming.Count > targetDescriptor.MaxNodeOutcomingLanes)
                    {
                        return false;
                    }
                    renderI &= invertIJ
                        && (
                             (isSegmentIinverted && netSegmentI.Info.m_backwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentI.Info.m_backwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes)
                           || (!isSegmentIinverted && netSegmentI.Info.m_forwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentI.Info.m_forwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes)
                           ) && netSegmentI.Info?.m_halfWidth < targetDescriptor.MaxRoadHalfWidth + 0.002f && netSegmentI.Info?.m_halfWidth > targetDescriptor.MinRoadHalfWidth - 0.002f;

                    renderJ &= !invertIJ && ((isSegmentJinverted && netSegmentJ.Info.m_backwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentJ.Info.m_backwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes)
                                          || (!isSegmentJinverted && netSegmentJ.Info.m_forwardVehicleLaneCount >= targetDescriptor.MinDirectionTrafficLanes && netSegmentJ.Info.m_forwardVehicleLaneCount <= targetDescriptor.MaxDirectionTrafflcLanes))
                                          && netSegmentJ.Info?.m_halfWidth < targetDescriptor.MaxRoadHalfWidth + 0.002f && netSegmentJ.Info?.m_halfWidth > targetDescriptor.MinRoadHalfWidth - 0.002f;

                    if (outcoming.Count == 1)
                    {
                        renderI &= !SegmentUtils.IsSameName(outcoming.First(), segmentIid, false, false, true, targetDescriptor.PlaceOnDistrictBorder, targetDescriptor.PlaceOnTunnelBridgeStart);
                        renderJ &= !SegmentUtils.IsSameName(outcoming.First(), segmentJid, false, false, true, targetDescriptor.PlaceOnDistrictBorder, targetDescriptor.PlaceOnTunnelBridgeStart);
                    }
                }
                else
                {
                    renderI &= ((invertIJ == isSegmentIinverted && netSegmentI.Info.m_hasBackwardVehicleLanes) || (invertIJ != isSegmentIinverted && netSegmentI.Info.m_hasForwardVehicleLanes)) && netSegmentI.Info?.m_halfWidth < targetDescriptor.MaxRoadHalfWidth + 0.002f && netSegmentI.Info?.m_halfWidth > targetDescriptor.MinRoadHalfWidth - 0.002f; ;
                    renderJ &= ((invertIJ != isSegmentJinverted && netSegmentJ.Info.m_hasBackwardVehicleLanes) || (invertIJ == isSegmentJinverted && netSegmentJ.Info.m_hasForwardVehicleLanes)) && netSegmentJ.Info?.m_halfWidth < targetDescriptor.MaxRoadHalfWidth + 0.002f && netSegmentJ.Info?.m_halfWidth > targetDescriptor.MinRoadHalfWidth - 0.002f; ;
                }


            }
            else
            {
                bool start = netSegmentI.m_startNode == nodeID;
                netSegmentI.CalculateCorner(segmentIid, true, start, false, out Vector3 startPos, out Vector3 startAng, out _);

                start = (netSegmentJ.m_startNode == nodeID);
                netSegmentJ.CalculateCorner(segmentJid, true, start, true, out Vector3 endPos, out Vector3 endAng, out _);

                NetSegment.CalculateMiddlePoints(startPos, -startAng, endPos, -endAng, true, true, out Vector3 rhs, out Vector3 lhs);
                Vector3 relativePos = (((rhs + lhs) * 0.5f) - data.m_position);
                platePosI = platePosJ = relativePos - relativePos.normalized + data.m_position;
                segmentIDirection.y = segmentJDirection.y = 0;
            }
            if (!renderI && !renderJ)
            {
                return false;
            }
            int id1 = subboardOffset++;
            int id2 = subboardOffset++;
            lock (Data.BoardsContainers)
            {
                FillCacheData(nodeID, controlBoardIdx, id1, segmentIid, platePosI, segmentIDirection, targetDescriptor, renderI);
                FillCacheData(nodeID, controlBoardIdx, id2, segmentJid, platePosJ, segmentJDirection, targetDescriptor, renderJ);
            }
            return true;
        }

        private static Vector3 CaculateCenterSegment(int nodeID, ref NetSegment segmentI, BoardInstanceRoadNodeXml descriptor, out float rotationOffsetZ, bool invertSide)
        {
            bool comingFromNode = (segmentI.m_startNode == nodeID);
            bool invertActive = (segmentI.m_flags & NetSegment.Flags.Invert) != 0;
            bool invertedSegment = invertActive != comingFromNode != (SimulationManager.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.False);
            invertSide ^= invertActive;
            Vector3 platePosI;
            Vector3 bezierPos = segmentI.GetBezier().Position(Mathf.Max(0, Mathf.Min(1, (descriptor.m_propPosition.x / 2) + 0.5f)));

            segmentI.GetClosestPositionAndDirection(bezierPos, out _, out Vector3 dir);
            int rotationOffsetSide = invertedSegment != invertSide ? 90 : -90;
            rotationOffsetZ = invertSide == invertActive ? 90 : -90;
            float rotation = dir.GetAngleXZ();

            Vector3 rotationVectorSide = VectorUtils.X_Y(KlyteMathUtils.DegreeToVector2(rotation + rotationOffsetSide));
            platePosI = bezierPos + ((rotationVectorSide * segmentI.Info.m_halfWidth) + (rotationVectorSide * descriptor.m_propPosition.z));
            platePosI.y += descriptor.m_propPosition.y;

            return platePosI;
        }

        private void FillCacheData(ushort nodeID, int controlBoardIdx, int boardId, ushort segmentIid, Vector3 platePosI, Vector3 segmentIDirection, BoardInstanceRoadNodeXml targetDescriptor, bool render)
        {

            Data.BoardsContainers[nodeID, controlBoardIdx, boardId] = new CacheRoadNodeItem();
            ref CacheRoadNodeItem refBoard = ref Data.BoardsContainers[nodeID, controlBoardIdx, boardId];

            float dir = Vector2.zero.GetAngleToPoint(VectorUtils.XZ(segmentIDirection));

            refBoard.m_streetDirection = -dir + 90 + segmentIDirection.y;
            refBoard.m_segmentId = segmentIid;
            refBoard.m_districtId = DistrictManager.instance.GetDistrict(NetManager.instance.m_segments.m_buffer[segmentIid].m_middlePosition);
            refBoard.m_districtParkId = DistrictManager.instance.GetPark(NetManager.instance.m_segments.m_buffer[segmentIid].m_middlePosition);
            refBoard.m_platePosition = platePosI;
            refBoard.m_renderPlate = render && targetDescriptor.Allows(refBoard.m_districtParkId, refBoard.m_districtId);
            refBoard.m_cachedColor = targetDescriptor.UseDistrictColor ? WTSHookable.GetDistrictColor(refBoard.m_districtId) : targetDescriptor.Descriptor.FixedColor ?? Color.white;
            refBoard.m_cachedContrastColor = KlyteMonoUtils.ContrastColor(refBoard.m_cachedColor);
            refBoard.m_distanceRef = Vector2.Distance(VectorUtils.XZ(refBoard.m_platePosition), WTSHookable.GetStartPoint());
            refBoard.m_distanceRefKm = Mathf.RoundToInt(refBoard.m_distanceRef / 1000);
            refBoard.m_currentDescriptor = targetDescriptor;
        }

        private static void GetNeigborSegment(ushort nodeID, ref NetNode data, ushort segmentIid, ref NetSegment netSegmentI, out Vector3 segmentIDirection, out Vector3 otherSegmentDirection, out ushort resultOtherSegment, out bool invertIJ)
        {
            segmentIDirection = (nodeID != netSegmentI.m_startNode) ? netSegmentI.m_endDirection : netSegmentI.m_startDirection;
            otherSegmentDirection = Vector3.zero;
            float resultAngle = -4f;
            resultOtherSegment = 0;
            for (int j = 0; j < 8; j++)
            {
                ushort segmentJid = data.GetSegment(j);
                if (segmentJid != 0 && segmentJid != segmentIid)
                {
                    ref NetSegment netSegmentCand = ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentJid];
                    if (netSegmentCand.Info != null)
                    {
                        Vector3 segmentJDirection = (nodeID != netSegmentCand.m_startNode) ? netSegmentCand.m_endDirection : netSegmentCand.m_startDirection;
                        float angle = (segmentIDirection.x * segmentJDirection.x) + (segmentIDirection.z * segmentJDirection.z);
                        if ((segmentJDirection.z * segmentIDirection.x) - (segmentJDirection.x * segmentIDirection.z) < 0f)
                        {
                            if (angle > resultAngle)
                            {
                                resultAngle = angle;
                                resultOtherSegment = segmentJid;
                                otherSegmentDirection = segmentJDirection;
                            }
                        }
                        else
                        {
                            angle = -2f - angle;
                            if (angle > resultAngle)
                            {
                                resultAngle = angle;
                                resultOtherSegment = segmentJid;
                                otherSegmentDirection = segmentJDirection;
                            }
                        }
                    }
                }
            }
            invertIJ = resultAngle < -2f;
        }

        protected InstanceID GetPropRenderID(ushort nodeId)
        {
            return new InstanceID
            {
                NetNode = nodeId
            };

        }
        #endregion

        #region routing data cache
        private readonly HashSet<ulong> m_validHashes = new HashSet<ulong>();

        private readonly HashSet<ulong>[] m_passingHashes = new HashSet<ulong>[NetManager.MAX_NODE_COUNT];

        public readonly CacheDestinationRoute[,,] m_destinationInfo = new CacheDestinationRoute[NetManager.MAX_NODE_COUNT, 8, 4];

        public CacheDestinationRoute GetTargetDestination(ulong hash, int destination) => m_destinationInfo[hash & (NetManager.MAX_NODE_COUNT - 1), ((NetManager.MAX_NODE_COUNT << 3) - 1) >> 15, destination];

        private void InvalidateNodeDestinationPaths(ushort nodeId)
        {
            m_updatedDestinations[nodeId] = false;
            FilterValid(ref m_passingHashes[nodeId]);
            m_passingHashes[nodeId].ForEach(x => m_updatedDestinations[x & (NetManager.MAX_NODE_COUNT - 1)] = false);
            InvalidateHashes(ref m_passingHashes[nodeId]);

        }

        private ulong RegisterHash(ushort nodeId, uint segmentIdx)
        {
            m_nextHash += 0x40000;
            ulong hash = m_nextHash | (nodeId & 0x7FFFFu) | ((segmentIdx & 3u) << 15);
            m_validHashes.Add(hash);
            return hash;
        }

        private void AddToPassingList(ref HashSet<ulong> passing, ushort hash) => (passing ??= new HashSet<ulong>()).Add(hash);
        private void InvalidateHashes(ref HashSet<ulong> targets)
        {
            m_validHashes.ExceptWith(targets);
            targets = null;
        }

        private void FilterValid(ref HashSet<ulong> source) => source.IntersectWith(m_validHashes);

        private ulong m_nextHash = 0;

        #endregion
    }
    public class CacheDestinationRoute
    {
        private ushort m_nodeId;
        private byte m_districtId;
        private byte m_parkId;
        private float m_distance;
        private int m_distanceKm;
        private string m_distanceMeanString;
    }

}
