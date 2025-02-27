﻿using ColossalFramework;
using ColossalFramework.Globalization;
using Klyte.Commons;
using Klyte.Commons.Utils;
using Klyte.WriteTheSigns.Rendering;
using Klyte.WriteTheSigns.Xml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using static Klyte.Commons.Utils.XmlUtils;

namespace Klyte.WriteTheSigns.Data
{
    [XmlRoot("PropLayoutData")]
    public class WTSPropLayoutData : WTSLibBaseData<WTSPropLayoutData, BoardDescriptorGeneralXml>
    {

        public override string SaveId => "K45_WTS_PropLayoutData";

        protected override void Save()
        {
            WTSRoadNodesData.Instance.ResetCacheDescriptors();
            base.Save();
        }

        public override void LoadDefaults()
        {
            base.LoadDefaults();
            ReloadAllPropsConfigurations();
        }

        public string[] FilterBy(string input, TextRenderingClass? renderClass) =>
            m_indexes
            .Where((x) => (renderClass == null || renderClass == m_savedDescriptorsSerialized[x.Value].m_allowedRenderClass) && (input.IsNullOrWhiteSpace() ? true : LocaleManager.cultureInfo.CompareInfo.IndexOf(x.Key, input, CompareOptions.IgnoreCase) >= 0))
            .OrderBy((x) => ((int)(4 - m_savedDescriptorsSerialized[x.Value].m_configurationSource)) + x.Key)
            .Select(x => x.Key)
            .ToArray();

        [XmlElement("descriptorsData")]
        public override ListWrapper<BoardDescriptorGeneralXml> SavedDescriptorsSerialized
        {
            get => new ListWrapper<BoardDescriptorGeneralXml>() { listVal = m_savedDescriptorsSerialized.Where(x => x.m_configurationSource == ConfigurationSource.CITY).ToList() };
            set => ReloadAllPropsConfigurations(value);
        }

        private static string DefaultFilename { get; } = $"{WTSController.m_defaultFileNamePropsXml}.xml";
        public void ReloadAllPropsConfigurations() => ReloadAllPropsConfigurations(null);
        private void ReloadAllPropsConfigurations(ListWrapper<BoardDescriptorGeneralXml> fromCity)
        {
            m_savedDescriptorsSerialized = fromCity?.listVal?.Select(x => { x.m_configurationSource = ConfigurationSource.CITY; return x; }).ToArray() ?? m_savedDescriptorsSerialized.Where(x => x.m_configurationSource == ConfigurationSource.CITY).ToArray();
            LogUtils.DoLog("LOADING PROPS CONFIG START -----------------------------");
            var errorList = new List<string>();
            LogUtils.DoLog($"DefaultBuildingsConfigurationFolder = {WTSController.DefaultPropsLayoutConfigurationFolder}");
            FileUtils.ScanPrefabsFolders<PropInfo>(DefaultFilename, LoadDescriptorsFromXml);
            foreach (string filename in Directory.GetFiles(WTSController.DefaultPropsLayoutConfigurationFolder, "*.xml"))
            {
                try
                {
                    if (CommonProperties.DebugMode)
                    {
                        LogUtils.DoLog($"Trying deserialize {filename}:\n{File.ReadAllText(filename)}");
                    }
                    using (FileStream stream = File.OpenRead(filename))
                    {
                        LoadDescriptorsFromXml(stream, null);
                    }
                }
                catch (Exception e)
                {
                    LogUtils.DoWarnLog($"Error Loading file \"{filename}\" ({e.GetType()}): {e.Message}\n{e}");
                    errorList.Add($"Error Loading file \"{filename}\" ({e.GetType()}): {e.Message}");
                }
            }

            if (errorList.Count > 0)
            {
                K45DialogControl.ShowModal(new K45DialogControl.BindProperties
                {
                    title = "WTS - Errors loading Files",
                    message = string.Join("\r\n", errorList.ToArray()),
                    useFullWindowWidth = true,
                    showButton1 = true,
                    textButton1 = "Okay...",
                    showClose = true

                }, (x) => true);

            }

            LogUtils.DoLog("LOADING PROPS CONFIG END -----------------------------");
            m_savedDescriptorsSerialized = m_savedDescriptorsSerialized
                .GroupBy(p => p.SaveName)
                .Select(g => g.OrderBy(x => -1 * (int)x.m_configurationSource).First())
                .ToArray();
            UpdateIndex();
        }

        private void LoadDescriptorsFromXml(FileStream stream, PropInfo info)
        {
            var serializer = new XmlSerializer(typeof(ListWrapper<BoardDescriptorGeneralXml>));

            LogUtils.DoLog($"trying deserialize: {info}");

            if (serializer.Deserialize(stream) is ListWrapper<BoardDescriptorGeneralXml> config)
            {
                var result = new List<BoardDescriptorGeneralXml>();
                foreach (var item in config.listVal)
                {
                    if (info != null)
                    {
                        string[] propEffName = info.name.Split(".".ToCharArray(), 2);
                        string[] xmlEffName = item.m_propName?.Split(".".ToCharArray(), 2);
                        if (propEffName?.Length == 2 && xmlEffName?.Length == 2 && xmlEffName[1] == propEffName[1])
                        {
                            item.m_propName = info.name;
                            item.m_configurationSource = ConfigurationSource.ASSET;
                            item.SaveName = propEffName[0] + "/" + item.SaveName;
                            result.Add(item);
                        }
                        else
                        {
                            LogUtils.DoWarnLog($"PROP NAME WAS NOT MATCHING PROP INFO! SKIPPING! (prop folder: {$"{info}" ?? "<GLOBAL>"}| descriptor: {item.SaveName} | item.m_propName: {item.m_propName})");
                        }
                    }
                    else if (item.m_propName == null)
                    {
                        LogUtils.DoErrorLog($"PROP NAME WAS NOT SET! (prop folder: {$"{info}" ?? "<GLOBAL>"}| descriptor: {item.SaveName})");
                        continue;
                    }
                    else
                    {
                        item.m_configurationSource = ConfigurationSource.GLOBAL;
                        result.Add(item);
                    }
                }
                m_savedDescriptorsSerialized = m_savedDescriptorsSerialized.Union(result).ToArray();
            }
            else
            {
                LogUtils.DoErrorLog("The file wasn't recognized as a valid descriptor!");
            }
        }

    }
}
