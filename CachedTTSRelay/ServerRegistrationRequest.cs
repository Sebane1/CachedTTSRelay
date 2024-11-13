using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CachedTTSRelay {
    public class ServerRegistrationRequest {
        private string _publicHostAddress = "";
        private string _port = "";
        private string _region = "";
        private string _alias = "";
        private string _uniqueIdentifier = "";
        private DateTime _lastResponse;

        public string PublicHostAddress { get => _publicHostAddress; set => _publicHostAddress = value; }
        public string Port { get => _port; set => _port = value; }
        public string Region { get => _region; set => _region = value; }
        public string Alias { get => _alias; set => _alias = value; }
        public string UniqueIdentifier { get => _uniqueIdentifier; set => _uniqueIdentifier = value; }
        public DateTime LastResponse { get => _lastResponse; set => _lastResponse = value; }
    }
}
