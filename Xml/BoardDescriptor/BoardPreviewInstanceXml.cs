﻿namespace Klyte.WriteTheSigns.Xml
{
    public class BoardPreviewInstanceXml : BoardInstanceXml
    {
        public string m_currentText = "TEST TEXT";
        public string m_overrideText = null;
        public BoardDescriptorGeneralXml Descriptor { get; internal set; } = new BoardDescriptorGeneralXml();
    }

}
