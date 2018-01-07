using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Trading.Core
{
    [DataContract]
    public class Candle
    {
        [DataMember]
        public string SecurityCode { get; set; }

        [DataMember]
        public DateTime DateTime { get; set; }

        [DataMember]
        public double ClosePrice { get; set; }

        [DataMember]
        public double Volume { get; set; }
    }

    [DataContract]
    public class Advice
    {
        [DataMember]
        public string SecurityCode { get; set; }

        [DataMember]
        public DateTime DateTime { get; set; }

        [DataMember]
        public double Price { get; set; }

        [DataMember]
        public double Position { get; set; }
    }

    public class InitTraderRequest
    {
        public string Firm;
        public string Portfolio;
        public List<string> Securities;
    }

    public class InitTraderResponse
    {
        public class PortfolioInfo
        {
            public string Portfolio;
            public double Amount;
        }

        public List<PortfolioInfo> Portfolios;
    }

    public class OpenPositionRequest
    {
        public string Portfolio;
        public string Security;
        public double Price;
        public double Position;
    }

    public interface ICandleService
    {
		//also possible IObservable<Candles>
        BlockingCollection<Candle> GetCandles(string security, CancellationToken token);
    }

    public interface ITraderService
    {
        void Terminal();

        Task<InitTraderResponse> Init(InitTraderRequest request);

        void ShowStatus();

        void OpenPosition(OpenPositionRequest request);
    }

    public interface IAdvisorService
    {
        List<string> GetSecurities();
		//also possible IObservable<Advice>
		BlockingCollection<Advice> GetAdvices(string security, CancellationToken token);
    }

    public interface IStrategyService
    {
        void Start();
        void Stop();
    }

    public class Client
    {
        [XmlAttribute]
        public string Key { get; set; }

        [XmlAttribute]
        public string Path { get; set; }

        [XmlAttribute]
        public string Login { get; set; }

        [XmlAttribute]
        public string Password { get; set; }

        [XmlAttribute]
        public string DdeServer { get; set; }

        [XmlAttribute]
        public string DllName { get; set; }

        [XmlAttribute]
        public string Firm { get; set; }

        [XmlAttribute]
        public string Portfolio { get; set; }

        [XmlAttribute]
        public double Amount { get; set; }

        [XmlAttribute]
        public double MaxAmount { get; set; }

        [XmlAttribute]
        public double Weight { get; set; }

        [XmlAttribute]
        public bool PublishCandles { get; set; }
    }
}
