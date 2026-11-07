using Orbis.V1;

namespace Orbis.Network
{
    public sealed class CommandReceipt
    {
        public readonly ulong Sequence;
        public readonly int Generation;
        public readonly string RequestId;
        public readonly string CityId;
        public readonly ulong Revision;
        public readonly float SentAt;
        public CommandReceipt(ulong sequence,int generation,string requestId,string cityId,ulong revision,float sentAt)
        { Sequence=sequence;Generation=generation;RequestId=requestId;CityId=cityId;Revision=revision;SentAt=sentAt; }
        public bool Accept(Envelope envelope,int generation)
        {
            if(envelope.Seq!=Sequence||generation!=Generation||envelope.Server==null) return false;
            if(envelope.Server.Result!=null&&envelope.Server.Result.RequestId!=RequestId) return false;
            if(envelope.Server.City!=null&&(envelope.Server.City.CityId!=CityId||envelope.Server.City.Revision<Revision)) return false;
            return true;
        }
    }
}
