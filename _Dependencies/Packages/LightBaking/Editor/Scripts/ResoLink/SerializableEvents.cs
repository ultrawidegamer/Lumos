using Unity.Plastic.Newtonsoft.Json;

namespace LightBakingResoLink {
    [System.Serializable]
    public class GetSlotMessage {
        [JsonProperty("$type")]
        public string Type = "getSlot";
        
        [JsonProperty("slotId")]
        public string SlotId = "Root";
        
        [JsonProperty("includeComponentData")]
        public bool IncludeComponentData = false;
        
        [JsonProperty("depth")]
        public int Depth = 0;
    }
}