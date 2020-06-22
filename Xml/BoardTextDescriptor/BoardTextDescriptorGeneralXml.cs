﻿using ColossalFramework;
using ColossalFramework.UI;
using FontStashSharp;
using Klyte.Commons.Interfaces;
using Klyte.Commons.Utils;
using System;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;

namespace Klyte.WriteTheSigns.Xml
{
    [XmlRoot("textDescriptor")]
    public class BoardTextDescriptorGeneralXml : ILibable
    {
        [XmlAttribute("textScale")]
        public float m_textScale = 1f;
        [XmlAttribute("maxWidth")]
        public float m_maxWidthMeters = 0;
        [XmlAttribute("applyOverflowResizingOnY")]
        public bool m_applyOverflowResizingOnY = false;


        [XmlAttribute("textAlign")]
        public UIHorizontalAlignment m_textAlign = UIHorizontalAlignment.Center;
        [XmlAttribute("textContent")]
        public TextType m_textType = TextType.Fixed;
        [XmlAttribute("destinationRelative")]
        public DestinationReference m_destinationRelative = DestinationReference.Self;
        [XmlAttribute("targetNodeRelative")]
        public int m_targetNodeRelative = 0;
        [XmlAttribute("fixedText")]
        public string m_fixedText = "Text";
        [XmlAttribute("spriteName")]
        public string m_spriteName = "";

        [XmlAttribute("overrideFont")] public string m_overrideFont;

        [XmlAttribute("allCaps")]
        public bool m_allCaps = false;
        [XmlAttribute("applyAbbreviations")]
        public bool m_applyAbbreviations = true;
        [XmlAttribute("prefix")]
        public string m_prefix = "";
        [XmlAttribute("suffix")]
        public string m_suffix = "";


        [XmlAttribute("saveName")]
        public string SaveName { get; set; }


        [XmlElement("PlacingSettings")]
        public PlacingSettings PlacingConfig { get; set; } = new PlacingSettings();

        [XmlElement("ColoringSettings")]
        public ColoringSettings ColoringConfig { get; set; } = new ColoringSettings();

        [XmlElement("IlluminationSettings")]
        public IlluminationSettings IlluminationConfig { get; set; } = new IlluminationSettings();

        [XmlElement("MultiItemSettings")]
        public SubItemSettings MultiItemSettings { get; set; } = new SubItemSettings();
        [XmlElement("BackgroundMeshSettings")]
        public BackgroundMesh BackgroundMeshSettings { get; set; } = new BackgroundMesh();


        public bool IsTextRelativeToSegment()
        {
            switch (m_textType)
            {
                case TextType.District:
                case TextType.DistrictOrPark:
                case TextType.Park:
                case TextType.ParkOrDistrict:
                case TextType.PostalCode:
                case TextType.StreetCode:
                case TextType.StreetNameComplete:
                case TextType.StreetPrefix:
                case TextType.StreetSuffix:
                case TextType.DistanceFromReference:
                    return true;
            }
            return false;
        }
        public bool IsMultiItemText()
        {
            switch (m_textType)
            {
                case TextType.LinesSymbols:
                    return true;
            }
            return false;
        }
        public bool IsSpriteText()
        {
            switch (m_textType)
            {
                case TextType.LinesSymbols:
                case TextType.GameSprite:
                    return true;
            }
            return false;
        }

        public class SubItemSettings
        {
            [XmlAttribute("subItemsPerRow")]
            public int SubItemsPerRow { get => m_subItemsPerRow; set => m_subItemsPerRow = Math.Max(1, Math.Min(value, 10)); }
            [XmlAttribute("subItemsPerColumn")]
            public int SubItemsPerColumn { get => m_subItemsPerColumn; set => m_subItemsPerColumn = Math.Max(1, Math.Min(value, 10)); }

            [XmlAttribute("verticalFirst")]
            public bool VerticalFirst { get; set; }
            [XmlAttribute("verticalAlign")]
            public UIVerticalAlignment VerticalAlign { get; set; } = UIVerticalAlignment.Top;


            [XmlElement("subItemSpacing")]
            public Vector2Xml SubItemSpacing { get; set; } = new Vector2Xml();
            [XmlIgnore]
            private int m_subItemsPerColumn;
            [XmlIgnore]
            private int m_subItemsPerRow;
        }
        public class PlacingSettings
        {
            [XmlAttribute("cloneInvertHorizontalAlign")]
            public bool m_invertYCloneHorizontalAlign;
            [XmlAttribute("clone180DegY")]
            public bool m_create180degYClone;
            [XmlElement("position")]
            public Vector3Xml Position { get; set; } = new Vector3Xml();
            [XmlElement("rotation")]
            public Vector3Xml Rotation { get; set; } = new Vector3Xml();
        }
        public class ColoringSettings
        {
            [XmlAttribute("useContrastColor")]
            public bool m_useContrastColor = true;
            [XmlIgnore]
            public Color m_defaultColor = Color.clear;
            [XmlAttribute("color")]
            public string ForceColor { get => m_defaultColor == Color.clear ? null : ColorExtensions.ToRGB(m_defaultColor); set => m_defaultColor = value.IsNullOrWhiteSpace() ? Color.clear : (Color)ColorExtensions.FromRGB(value); }

        }

        public class IlluminationSettings
        {
            [XmlAttribute("type")]
            public MaterialType IlluminationType { get; set; } = MaterialType.OPAQUE;
            [XmlAttribute("strength")]
            public float IlluminationStrength { get; set; } = 1;
            [XmlAttribute("blinkType")]
            public BlinkType BlinkType { get; set; } = BlinkType.None;
            [XmlElement("customBlinkParams")]
            public Vector4Xml CustomBlink { get; set; } = new Vector4Xml();
            [XmlAttribute("requiredFlags")]
            public int m_requiredFlags;
            [XmlAttribute("forbiddenFlags")]
            public int m_forbiddenFlags;

        }

        public class BackgroundMesh
        {
            [XmlElement("size")]
            public Vector2Xml Size { get; set; } = new Vector2Xml();

            [XmlIgnore]
            public Color BackgroundColor { get => m_cachedColor; set => m_cachedColor = value; }
            [XmlIgnore]
            private Color m_cachedColor;
            [XmlAttribute("color")]
            public string BgColorStr { get => m_cachedColor == null ? null : ColorExtensions.ToRGB(BackgroundColor); set => BackgroundColor = value.IsNullOrWhiteSpace() ? default : ColorExtensions.FromRGB(value); }
        }
    }



    public enum BlinkType
    {
        None,
        Blink_050_050,
        MildFade_0125_0125,
        MediumFade_500_500,
        StrongBlaze_0125_0125,
        StrongFade_250_250,
        Blink_025_025,
        Custom
    }


}
