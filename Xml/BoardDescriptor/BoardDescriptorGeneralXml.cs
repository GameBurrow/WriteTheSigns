﻿using ColossalFramework;
using Klyte.Commons.Interfaces;
using Klyte.Commons.Utils;
using Klyte.WriteTheCity.Rendering;
using System;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace Klyte.WriteTheCity.Xml
{

    public class BoardDescriptorGeneralXml : ILibable
    {
        [XmlAttribute("propName")]
        public string m_propName;

        [XmlAttribute("availability")]
        public TextRenderingClass m_allowedRenderClass = TextRenderingClass.RoadNodes;

        [XmlIgnore]
        public Color? FixedColor { get => m_cachedColor; set => m_cachedColor = value; }
        [XmlIgnore]
        private Color? m_cachedColor;
        [XmlAttribute("fixedColor")]
        public string FixedColorStr { get => m_cachedColor == null ? null : ColorExtensions.ToRGB(FixedColor ?? Color.clear); set => FixedColor = value.IsNullOrWhiteSpace() ? null : (Color?)ColorExtensions.FromRGB(value); }

        [XmlAttribute("fontName")]
        public string FontName { get; set; }

        [XmlAttribute("saveName")]
        public string SaveName { get; set; }

        [XmlElement("textDescriptor")]
        public BoardTextDescriptorGeneralXml[] m_textDescriptors = new BoardTextDescriptorGeneralXml[0];


        public Matrix4x4 TextMatrixTranslation(int idx) => Matrix4x4.Translate(m_textDescriptors[idx].m_textRelativePosition);
        public Matrix4x4 TextMatrixRotation(int idx) => Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(m_textDescriptors[idx].m_textRelativeRotation), Vector3.one);
    }


}
