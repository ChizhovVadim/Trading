using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Trading.Advisor;
using Trading.Core;

namespace Trading
{
    static class Serializer
    {
        public static T Load<T>(string filePath)
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = (T)serializer.Deserialize(fileStream);
                return result;
            }
        }
    }

    public class StrategySettings
    {
        public AdvisorInfo AdvisorInfo { get; set; }

        public List<StrategyConfig> StrategyConfigs { get; set; }

        public HistoryCandleProviderSettings HistoryCandleProviderSettings { get; set; }

        public List<Client> Clients { get; set; }

        public string StartTime { get; set; }

        public string LogPath { get; set; }
    }

    public class AdvisorInfo
    {
		[XmlAttribute]
		public bool IsLive { get; set; }
        [XmlAttribute]
        public bool IsLocal { get; set; }
        [XmlAttribute]
        public string Url { get; set; }
    }
}
