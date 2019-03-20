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
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using static BuildingInfo;
using static Klyte.Commons.Utils.KlyteUtils;
using static Klyte.DynamicTextBoards.Overrides.BoardGeneratorBuildings;

namespace Klyte.DynamicTextBoards.Overrides
{

    public class BoardGeneratorBuildings : BoardGeneratorParent<BoardGeneratorBuildings, BoardBunchContainerBuilding, CacheControlTransportBuilding, BasicRenderInformation, BoardDescriptorStations, BoardTextDescriptor, ushort>
    {

        private Dictionary<String, BoardDescriptorStations[]> loadedDescriptors;

        private LineDescriptor[] m_linesDescriptors;
        private UpdateFlagsBuildings[] m_updateData;
        private Dictionary<string, StopPointDescriptorLanes[]> m_buildingStopsDescriptor = new Dictionary<string, StopPointDescriptorLanes[]>();

        public override int ObjArraySize => BuildingManager.MAX_BUILDING_COUNT;

        private UIDynamicFont m_font;

        public override UIDynamicFont DrawFont => m_font;

        #region Initialize
        public override void Initialize()
        {
            BuildSurfaceFont(out m_font, "Arial");
            LoadAllBuildingConfigurations();


            TransportManagerOverrides.eventOnLineUpdated += onLineUpdated;
            NetManagerOverrides.eventNodeChanged += onNodeChanged;
            TransportManager.instance.eventLineColorChanged += onLineUpdated;
            InstanceManagerOverrides.eventOnBuildingRenamed += onBuildingNameChanged;

            #region Hooks
            var postRenderMeshs = GetType().GetMethod("AfterRenderMeshes", allFlags);
            doLog($"Patching=> {postRenderMeshs}");
            AddRedirect(typeof(BuildingAI).GetMethod("RenderMeshes", allFlags), null, postRenderMeshs);
            #endregion
        }

        public void LoadAllBuildingConfigurations()
        {
            ScanPrefabsFolders($"{DynamicTextBoardsMod.defaultFileNameXml}.xml", LoadDescriptorsFromXml);
            foreach (var filename in Directory.GetFiles(DynamicTextBoardsMod.defaultBuildingsConfigurationFolder, "*.xml"))
            {
                using (var stream = File.OpenRead(filename))
                {
                    LoadDescriptorsFromXml(stream);
                }
            }

            m_updateData = new UpdateFlagsBuildings[BuildingManager.MAX_BUILDING_COUNT];
            m_linesDescriptors = new LineDescriptor[TransportManager.MAX_LINE_COUNT];
            m_boardsContainers = new BoardBunchContainerBuilding[BuildingManager.MAX_BUILDING_COUNT];
        }

        private void LoadDescriptorsFromXml(FileStream stream)
        {
            var serializer = new XmlSerializer(typeof(BuildingConfigurationSerializer<BoardDescriptorStations, BoardTextDescriptor>));


            if (serializer.Deserialize(stream) is BuildingConfigurationSerializer<BoardDescriptorStations, BoardTextDescriptor> config)
            {
                if (loadedDescriptors == null) loadedDescriptors = new Dictionary<string, BoardDescriptorStations[]>();
                loadedDescriptors[config.m_buildingName] = config.m_boardDescriptors;
            }
        }

        private void onNodeChanged(ushort id)
        {
            var buildingId = NetNode.FindOwnerBuilding(id, 56f);
            if (buildingId > 0 && m_boardsContainers[buildingId] != null)
            {
                m_boardsContainers[buildingId].m_linesUpdateFrame = 0;
            }
        }

        protected override void OnTextureRebuilt()
        {
            m_updateData = new UpdateFlagsBuildings[BuildingManager.MAX_BUILDING_COUNT];
        }


        private void onLineUpdated(ushort lineId)
        {
            //doLog("onLineUpdated");
            m_linesDescriptors[lineId] = default(LineDescriptor);
        }
        private void onBuildingNameChanged(ushort id)
        {
            //doLog("onBuildingNameChanged");
            m_updateData[id].m_nameMesh = false;
        }
        #endregion


        public static void AfterRenderMeshes(BuildingAI __instance, RenderManager.CameraInfo cameraInfo, ushort buildingID, ref Building data, int layerMask, ref RenderManager.Instance instance)
        {
            BoardGeneratorBuildings.instance.AfterRenderMeshesImpl(cameraInfo, buildingID, ref data, layerMask, ref instance, __instance);
        }

        public void AfterRenderMeshesImpl(RenderManager.CameraInfo cameraInfo, ushort buildingID, ref Building data, int layerMask, ref RenderManager.Instance renderInstance, BuildingAI __instance)
        {
            if (!loadedDescriptors.ContainsKey(data.Info.name) || loadedDescriptors[data.Info.name].Length == 0)
            {
                return;
            }
            if (m_boardsContainers[buildingID] == null)
            {
                m_boardsContainers[buildingID] = new BoardBunchContainerBuilding();
            }
            if (m_boardsContainers[buildingID]?.m_boardsData?.Count() != loadedDescriptors[data.Info.name].Length)
            {
                m_boardsContainers[buildingID].m_boardsData = new CacheControlTransportBuilding[loadedDescriptors[data.Info.name].Length];
                m_updateData[buildingID].m_nameMesh = false;
            }

            UpdateLinesBuilding(buildingID, ref data, m_boardsContainers[buildingID]);
            for (var i = 0; i < loadedDescriptors[data.Info.name].Length; i++)
            {
                var descriptor = loadedDescriptors[data.Info.name][i];
                if (m_boardsContainers[buildingID].m_boardsData[i] == null) m_boardsContainers[buildingID].m_boardsData[i] = new CacheControlTransportBuilding();
                RenderPropMesh(ref m_boardsContainers[buildingID].m_boardsData[i].m_cachedProp, cameraInfo, buildingID, i, 0, layerMask, data.m_angle, renderInstance.m_dataMatrix1.MultiplyPoint(descriptor.m_propPosition), renderInstance.m_dataVector3, ref descriptor.m_propName, descriptor.m_propRotation, descriptor.PropScale, ref descriptor, out Matrix4x4 propMatrix, out bool rendered);
                if (rendered && descriptor.m_textDescriptors != null)
                {
                    for (int j = 0; j < descriptor.m_textDescriptors?.Length; j++)
                    {
                        MaterialPropertyBlock materialBlock = Singleton<PropManager>.instance.m_materialBlock;
                        materialBlock.Clear();

                        RenderTextMesh(cameraInfo, buildingID, i, j, ref descriptor, propMatrix, ref descriptor.m_textDescriptors[j], ref m_boardsContainers[buildingID].m_boardsData[i], materialBlock);
                    }
                }
            }
        }



        #region Upadate Data
        protected override BasicRenderInformation GetOwnNameMesh(ushort buildingID, int boardIdx, int secIdx)
        {
            if (m_boardsContainers[buildingID].m_nameSubInfo == null || !m_updateData[buildingID].m_nameMesh)
            {
                RefreshNameData(ref m_boardsContainers[buildingID].m_nameSubInfo, BuildingManager.instance.GetBuildingName(buildingID, new InstanceID()) ?? "DUMMY!!!!!");
                m_updateData[buildingID].m_nameMesh = true;
            }
            return m_boardsContainers[buildingID].m_nameSubInfo;

        }
        protected void UpdateLinesBuilding(ushort buildingID, ref Building data, BoardBunchContainerBuilding bbcb)
        {
            if (bbcb.m_platformToLine == null || (bbcb.m_ordenedLines?.Length > 0 && bbcb.m_linesUpdateFrame < bbcb.m_ordenedLines.Select((x) => m_linesDescriptors[x]?.m_lastUpdate ?? 0).Max()))
            {
                if (!m_buildingStopsDescriptor.ContainsKey(data.Info.name))
                {
                    m_buildingStopsDescriptor[data.Info.name] = MapStopPoints(data.Info);
                    //m_buildingStopsDescriptor[data.Info.name + "PLAT"] = GetAllPlatforms(data.Info.m_buildingAI);
                }

                var platforms = m_buildingStopsDescriptor[data.Info.name].Select((v, i) => new { Key = i, Value = v }).ToDictionary(o => o.Key, o => o.Value);
                //var platformsPlat = m_buildingStopsDescriptor[data.Info.name + "PLAT"].Select((v, i) => new { Key = i, Value = v }).ToDictionary(o => o.Key, o => o.Value);
                if (platforms.Count == 0)
                {
                    bbcb.m_ordenedLines = new ushort[0];
                    bbcb.m_platformToLine = new ushort[0][];
                }
                else
                {

                    List<Quad2> boundaries = new List<Quad2>();
                    var subBuilding = buildingID;
                    var allnodes = new List<ushort>();
                    while (subBuilding > 0)
                    {
                        boundaries.Add(GetBounds(ref BuildingManager.instance.m_buildings.m_buffer[subBuilding]));
                        var node = BuildingManager.instance.m_buildings.m_buffer[subBuilding].m_netNode;
                        while (node > 0)
                        {
                            allnodes.Add(node);
                            node = NetManager.instance.m_nodes.m_buffer[node].m_nextBuildingNode;
                        }
                        subBuilding = BuildingManager.instance.m_buildings.m_buffer[subBuilding].m_subBuilding;
                    }
                    foreach (ushort node in allnodes)
                    {
                        if (!boundaries.Any(x => x.Intersect(NetManager.instance.m_nodes.m_buffer[node].m_position)))
                        {
                            for (var segIdx = 0; segIdx < 8; segIdx++)
                            {
                                var segmentId = NetManager.instance.m_nodes.m_buffer[node].GetSegment(segIdx);
                                if (segmentId != 0 && allnodes.Contains(NetManager.instance.m_segments.m_buffer[segmentId].GetOtherNode(node)))
                                {


                                    boundaries.Add(GetBounds(
                                        NetManager.instance.m_nodes.m_buffer[NetManager.instance.m_segments.m_buffer[segmentId].m_startNode].m_position,
                                        NetManager.instance.m_nodes.m_buffer[NetManager.instance.m_segments.m_buffer[segmentId].m_endNode].m_position,
                                        NetManager.instance.m_segments.m_buffer[segmentId].Info.m_halfWidth)
                                        );
                                }
                            }
                        }
                    }
                    var nearStops = KlyteUtils.FindNearStops(data.m_position, ItemClass.Service.PublicTransport, ItemClass.Service.PublicTransport, VehicleInfo.VehicleType.None, true, 400f, out List<float> dist, out List<Vector3> absolutePos, boundaries);
                    if (nearStops.Count > 0)
                    {
                        bbcb.m_platformToLine = new ushort[m_buildingStopsDescriptor[data.Info.name].Length][];
                        var nearStopsParsed = nearStops.Select((x, i) => new { stopId = x, relPos = DTBUtils.CalculatePositionRelative(absolutePos[i], BuildingManager.instance.m_buildings.m_buffer[buildingID].m_angle, BuildingManager.instance.m_buildings.m_buffer[buildingID].m_position) })
                         .Select((y, i) => Tuple.New(platforms.Where((x, j) =>
                         {
                             if (x.Value.vehicleType != TransportManager.instance.m_lines.m_buffer[NetManager.instance.m_nodes.m_buffer[y.stopId].m_transportLine].Info.m_vehicleType) return false;
                             //var relOrg = CalculatePositionRelative(absolutePos[i], BuildingManager.instance.m_buildings.m_buffer[buildingID].m_angle, BuildingManager.instance.m_buildings.m_buffer[buildingID].m_position);
                             var distance = x.Value.platformLine.DistanceSqr(y.relPos, out float k);
                             doLog($"[{BuildingManager.instance.m_buildings.m_buffer[buildingID].Info.name}]x = {x.Key} ({x.Value.platformLine.a} {x.Value.platformLine.b} {x.Value.platformLine.c} {x.Value.platformLine.d}) (w= {x.Value.width}) {x.Value.vehicleType}\t| relOrg {y.relPos} \t| {distance} \t|dy = { x.Value.platformLine.GetBounds().center.y - y.relPos.y}");
                             var sqrWidth = x.Value.width * x.Value.width;

                             return Mathf.Abs(distance - sqrWidth) < 0.1f * sqrWidth && x.Value.platformLine.GetBounds().center.y - y.relPos.y < 1f;
                         }).FirstOrDefault().Key, NetManager.instance.m_nodes.m_buffer[y.stopId].m_transportLine));

                        foreach (var nearStopsParsedItem in nearStopsParsed.Select(x => x.First).Distinct())
                        {
                            bbcb.m_platformToLine[nearStopsParsedItem] = nearStopsParsed.Where(x => x.First == nearStopsParsedItem).Select(x => x.Second).ToArray();
                        }
                        var uniqueLines = nearStopsParsed.Select(x => x.Second).Distinct().ToList();
                        uniqueLines.Sort((a, b) => VehicleToPriority(TransportManager.instance.m_lines.m_buffer[a].Info.m_vehicleType).CompareTo(VehicleToPriority(TransportManager.instance.m_lines.m_buffer[b].Info.m_vehicleType)));
                        bbcb.m_ordenedLines = uniqueLines.ToArray();
                        //doLog($"updatedIdsColors {nearStops.Count} [{string.Join(",", nearStops.Select(x => x.ToString()).ToArray())}], [{string.Join(",", dist.Select(x => x.ToString()).ToArray())}], ");
                    }
                }
                bbcb.m_linesUpdateFrame = SimulationManager.instance.m_currentTickIndex;
            }
        }

        #endregion

        public override Color GetColor(ushort buildingID, int boardIdx, int textIdx, BoardDescriptorStations descriptor)
        {
            var targetPlatforms = descriptor.m_platforms;
            foreach (var platform in targetPlatforms)
            {
                if (m_boardsContainers[buildingID].m_platformToLine != null && m_boardsContainers[buildingID].m_platformToLine.ElementAtOrDefault(platform) != null)
                {
                    var line = m_boardsContainers[buildingID].m_platformToLine[platform].ElementAtOrDefault(0);
                    if (line != 0)
                    {
                        if (m_linesDescriptors[line] == null)
                        {
                            UpdateLine(line);
                        }
                        return m_linesDescriptors[line].m_lineColor;
                    }
                }
            }
            return Color.white;
        }
        public override Color GetContrastColor(ushort buildingID, int boardIdx, int textIdx, BoardDescriptorStations descriptor)
        {
            var targetPlatforms = descriptor.m_platforms;
            foreach (var platform in targetPlatforms)
            {
                if (m_boardsContainers[buildingID].m_platformToLine != null && m_boardsContainers[buildingID].m_platformToLine.ElementAtOrDefault(platform) != null && m_boardsContainers[buildingID].m_platformToLine[platform].Length > 0)
                {
                    var line = m_boardsContainers[buildingID].m_platformToLine[platform].ElementAtOrDefault(0);
                    if (line != 0)
                    {
                        if (m_linesDescriptors[line] == null)
                        {
                            UpdateLine(line);
                        }
                        return m_linesDescriptors[line].m_contrastColor;
                    }
                }
            }
            return Color.black;
        }

        private void UpdateLine(ushort lineId)
        {
            m_linesDescriptors[lineId] = new LineDescriptor
            {
                m_lineColor = TransportManager.instance.GetLineColor(lineId),
                m_lastUpdate = SimulationManager.instance.m_currentTickIndex
            };

            m_linesDescriptors[lineId].m_contrastColor = KlyteUtils.contrastColor(m_linesDescriptors[lineId].m_lineColor);

        }

        protected override InstanceID GetPropRenderID(ushort buildingID)
        {
            InstanceID result = default(InstanceID);
            result.Building = buildingID;
            return result;
        }

        private class LineDescriptor
        {
            public Color m_lineColor;
            public Color m_contrastColor;
            public BasicRenderInformation m_lineName;
            public BasicRenderInformation m_lineNumber;
            public uint m_lastUpdate;
        }

        private struct UpdateFlagsBuildings
        {
            public bool m_nameMesh;
        }

        public class CacheControlTransportBuilding : CacheControl
        {
        }

        public class BoardBunchContainerBuilding : IBoardBunchContainer<CacheControlTransportBuilding, BasicRenderInformation>
        {
            public ushort[][] m_platformToLine;
            public ushort[] m_ordenedLines;
            public uint m_linesUpdateFrame;
        }

        public static void GenerateDefaultBuildingsConfiguration()
        {
            var fileContent = GenerateDefaultDictionary().Select(x => new BuildingConfigurationSerializer<BoardDescriptorStations, BoardTextDescriptor> { m_buildingName = x.Key, m_boardDescriptors = x.Value.ToArray() }).ToArray();
            var serializer = new XmlSerializer(typeof(BuildingConfigurationSerializer<BoardDescriptorStations, BoardTextDescriptor>));
            foreach (var item in fileContent)
            {
                var filePath = DynamicTextBoardsMod.defaultBuildingsConfigurationFolder + Path.DirectorySeparatorChar + $"{DynamicTextBoardsMod.defaultFileNameXml}_{item.m_buildingName}.xml";
                if (!File.Exists(filePath))
                {
                    var stream = File.OpenWrite(filePath);
                    try
                    {
                        XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                        ns.Add("", "");
                        stream.SetLength(0);
                        serializer.Serialize(stream, item, ns);
                    }
                    finally
                    {
                        stream.Close();
                    }
                }
            }
        }

        private static Dictionary<string, List<BoardDescriptorStations>> GenerateDefaultDictionary()
        {

            var basicEOLTextDescriptor = new BoardTextDescriptor[]{
                             new BoardTextDescriptor{
                                m_textRelativePosition = new Vector3(0,4.7f, -0.13f) ,
                                m_textRelativeRotation = Vector3.zero,
                                m_maxWidthMeters = 15.5f
                             },
                             new BoardTextDescriptor{
                                m_textRelativePosition = new Vector3(0,4.7f,0.02f),
                                m_textRelativeRotation = new Vector3(0,180,0),
                                m_maxWidthMeters = 15.5f
                             },
                        };
            var basicWallTextDescriptor = new BoardTextDescriptor[]{
                             new BoardTextDescriptor{
                                m_textRelativePosition =new Vector3(0,0.4F,-0.08f) ,
                                m_textRelativeRotation = Vector3.zero,
                                m_maxWidthMeters = 15.5f
                             },
                        };
            var basicTotem = new BoardTextDescriptor[]{
                             new BoardTextDescriptor{
                                m_textRelativePosition =new Vector3(-0.01f,2.2f,-0.09f) ,
                                m_textRelativeRotation = new Vector3(0,330,270),
                                m_maxWidthMeters = 2.5f,
                                m_textScale = 0.5f,
                                m_dayEmissiveMultiplier = 0f,
                                m_nightEmissiveMultiplier = 7f,
                                m_useContrastColor = false,
                                m_defaultColor = Color.white
                             },
                             new BoardTextDescriptor{
                                m_textRelativePosition =new Vector3(-0.01f,2.2f,0.09f) ,
                                m_textRelativeRotation = new Vector3(0,210,270),
                                m_maxWidthMeters = 2.5f,
                                m_textScale = 0.5f,
                                m_dayEmissiveMultiplier = 0f,
                                m_nightEmissiveMultiplier = 7f,
                                m_useContrastColor = false,
                                m_defaultColor = Color.white
                             },
                             new BoardTextDescriptor{
                                m_textRelativePosition =new Vector3(0.14f,2.2f,0f) ,
                                m_textRelativeRotation = new Vector3(0,90,270),
                                m_maxWidthMeters = 2.5f,
                                m_textScale = 0.5f,
                                m_dayEmissiveMultiplier = 0f,
                                m_nightEmissiveMultiplier = 7f,
                                m_useContrastColor = false,
                                m_defaultColor = Color.white
                             },
                        };

            return new Dictionary<string, List<BoardDescriptorStations>>
            {
                ["Train Station"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679674753.BoardV6_Data",
                        m_propPosition= new Vector3(8f,6f,0.5F),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{ 0 }
},
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679674753.BoardV6_Data",
                        m_propPosition= new Vector3(-13.5f,6f,0.5F),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{ 0 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679674753.BoardV6_Data",
                        m_propPosition= new Vector3(0,5f,-16),
                        m_propRotation= new Vector3(0,180,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{ 1 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName = "1679674753.BoardV6_Data",
                        m_propPosition = new Vector3(-14, 8f, 22),
                        m_propRotation= new Vector3(0,180,0),
                        m_textDescriptors =basicWallTextDescriptor,

                        m_platforms = new int[]{ 0,1 }
                    },
                },
                ["End of the line Trainstation"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(48,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 2 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(32,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{3,4 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(16,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{5,6}
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 7,8 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-48,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 13 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-32,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 11,12 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-16,-1.5f,-48),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 9, 10 }
                    },

                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(48,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{2 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(32,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 3,4 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(16,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 5,6 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 7,8 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-48,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 13 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-32,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 11,12 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-16,-1.5f,-80),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 9, 10 }
                    },
                     new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(48,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 2 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(32,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 3,4 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(16,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{5,6 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 7,8 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-48,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 13 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-32,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 11,12 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-16,-1.5f,-106),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 9, 10 }
                    },
                     new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(48,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{2 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(32,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 3,4 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(16,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 5,6 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 7,8 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-48,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{13 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-32,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{11,12 }
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-16,-1.5f,-133),
                        m_propRotation= new Vector3(0,90,0),
                        m_textDescriptors =basicEOLTextDescriptor,

                        m_platforms = new int[]{ 9,10 }
                    },
                },
                ["Large Trainstation"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-0),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{2},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{3,4},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-32),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{5,6 },

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-48),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{7,8},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-64),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{9,10},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-80),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{11,12},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(41,-1.5f,-95.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{13},
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-0),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{2},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{4,3},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-32),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{6,5},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-48),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{8,7},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-64),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{10,9},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-80),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{12,11},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(17,-1.5f,-95.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{13},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-0),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{2},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{3,4},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-32),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{5,6},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-48),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{7,8},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-64),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{9,10},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-80),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{11,12},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-41,-1.5f,-95.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{13},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-0),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{2},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{4,3},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-32),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{6,5},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-48),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{8,7},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-64),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{10,9},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-80),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{12,11},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-17,-1.5f,-95.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{13},

                    },
                },
                ["Metro Entrance"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679686240.Metro Totem_Data",
                        m_propPosition= new Vector3(4,0,4),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicTotem,
                        m_platforms = new int[]{0,1},

                    },
                },
                ["Monorail Station Standalone"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,6,-0.05f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{0,1},

                    },
                },
                ["Monorail Station Avenue"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0.05f,6,0),
                        m_propRotation= new Vector3(0,270,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{0,1},

                    },
                },
                ["Monorail Bus Hub"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0.05f,6,0),
                        m_propRotation= new Vector3(0,270,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{0,1},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(29.5f,-1,4),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{6,8,10},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(29.5f,-1,-4),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{6,4,2},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-29.5f,-1,4),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{7,9,11},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-29.5f,-1,-4),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{7,5,3},

                    },
                },
                ["Monorail Train Metro Hub"] = new List<BoardDescriptorStations>
                {
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,8f,-3),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{5},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,5.5f,12),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{2,3},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,8f,27),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{0},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName = "1679674753.BoardV6_Data",
                        m_propPosition = new Vector3(16, 4f, -0.75f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{6},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName = "1679674753.BoardV6_Data",
                        m_propPosition = new Vector3(-16, 4f, -0.75f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{6},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName = "1679674753.BoardV6_Data",
                        m_propPosition = new Vector3(44, 4f, -0.75f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{6},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName = "1679674753.BoardV6_Data",
                        m_propPosition = new Vector3(-44, 4f, -0.75f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicWallTextDescriptor,
                        m_platforms = new int[]{6},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(52,-2.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{7,8},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-52,-2.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{8,7},
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,-2.5f,-16),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{7,8},
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(52,-2.5f,-31.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{9},

                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(-52,-2.5f,-31.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{9}
                    },
                    new BoardDescriptorStations

                    {
                        m_propName=    "1679676810.BoardV6plat_Data",
                        m_propPosition= new Vector3(0,-2.5f,-31.5f),
                        m_propRotation= new Vector3(0,0,0),
                        m_textDescriptors =basicEOLTextDescriptor,
                        m_platforms = new int[]{9}

                    }
                }
            };
        }

    }

    public class BoardDescriptorStations : BoardDescriptorParent<BoardDescriptorStations, BoardTextDescriptor>

    {
        [XmlAttribute("platforms")]
        public int[] m_platforms = new int[0];
        [XmlAttribute("showIfNoLine")]
        public bool m_showIfNoLine = true;
    }

}
