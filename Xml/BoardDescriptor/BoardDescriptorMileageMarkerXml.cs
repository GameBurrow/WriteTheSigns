﻿using System.Xml.Serialization;

namespace Klyte.DynamicTextProps.Xml
{
    [XmlRoot("mileageMarkerDescriptor")]
    public class BoardDescriptorMileageMarkerXml : BoardInstanceXml
    {

        [XmlAttribute("useMiles")]
        public bool UseMiles { get; set; }

    }


}
