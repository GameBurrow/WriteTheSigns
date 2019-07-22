﻿using Klyte.Commons.Utils;
using System.Xml.Serialization;
using UnityEngine;
using System.Xml;
using Klyte.DynamicTextBoards.Libraries;

namespace Klyte.DynamicTextBoards.Overrides
{

    public partial class BoardGeneratorHighwaySigns
    {
        public class BoardDescriptorHigwaySignXml : BoardDescriptorParentXml<BoardDescriptorHigwaySignXml, BoardTextDescriptorHighwaySignsXml>, ILibable
        {
            [XmlAttribute("inverted")]
            public bool m_invertSign = false;
            [XmlAttribute("segmentPosition")]
            public float m_segmentPosition = 0.5f;
            [XmlIgnore]
            public Color m_color = Color.white;
            [XmlAttribute("color")]
            public string ColorStr
            {
                get => ColorExtensions.ToRGB(m_color);
                set => m_color = ColorExtensions.FromRGB(value);
            }
            [XmlAttribute("saveName")]
            public string SaveName { get; set; }
        }

    }
}
