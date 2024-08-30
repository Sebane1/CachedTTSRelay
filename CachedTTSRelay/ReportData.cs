using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;


namespace RoleplayingVoiceDalamud.Datamining {
    public class ReportData {
        private ushort territoryId;
        public string speaker { get; set; }
        public string sentence { get; set; }
        public ulong npcid { get; set; }
        public int body { get; set; }
        public bool gender { get; set; }
        public byte race { get; set; }
        public byte tribe { get; set; }
        public byte eyes { get; set; }
        public byte folder { get; set; }
        public string user { get; set; }
        public ushort TerritoryId { get => territoryId; set => territoryId = value; }
        public string Note { get; set; }

        public ReportData() {
        }
        public ReportData(string name, string message, uint objectId, int body, bool gender, byte race, byte tribe, byte eyes, ushort territoryId, string note) {
            speaker = name;
            sentence = message;
            npcid = objectId;
            this.body = body;
            this.gender = gender;
            this.race = race;
            this.tribe = tribe;
            this.eyes = eyes;
            this.territoryId = territoryId;
            this.Note = note;
            user = "ArtemisRoleplayingKit";
        }
    }
}
