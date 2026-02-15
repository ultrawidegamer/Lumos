using System.Collections.Generic;
using Unity.Plastic.Newtonsoft.Json;
using UnityEngine;

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

    [System.Serializable]
    public class GetComponentMessage {
        [JsonProperty("$type")]
        public string Type = "getComponent";
        
        [JsonProperty("componentId")]
        public string ComponentId;
    }

    [System.Serializable]
    public class SlotData {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("name")]
        public ValueWithId<string> Name;

        [JsonProperty("tag")]
        public ValueWithId<string> Tag;

        [JsonProperty("isActive")]
        public ValueWithId<bool> IsActive;

        [JsonProperty("isPersistent")]
        public ValueWithId<bool> IsPersistent;

        [JsonProperty("orderOffset")]
        public ValueWithId<long> OrderOffset;

        [JsonProperty("position")]
        public ValueWithId<Vector3> Position;

        [JsonProperty("rotation")]
        public ValueWithId<Vector4> Rotation;

        [JsonProperty("scale")]
        public ValueWithId<Vector3> Scale;

        [JsonProperty("children")]
        public SlotData[] Children;

        [JsonProperty("components")]
        public ComponentData[] Components;
    }

    [System.Serializable]
    public class ComponentData {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("componentType")]
        public string Type;

        [JsonProperty("members")]
        public Dictionary<string, ComponentMemberData> Members;

        [JsonProperty("isReferenceOnly")]
        public bool IsReferenceOnly;
    }

    [System.Serializable]
    public class ComponentMemberData {
        [JsonProperty("$type")]
        public string Type;

        [JsonProperty("value")]
        public dynamic Value;

        [JsonProperty("id")]
        public string Id;

        [JsonProperty("targetId")]
        public string TargetId;
    }

    [System.Serializable]
    public class SlotInfo {
        public SlotData Data;
        public string[] Path;
        public string MeshId;
    }

    [System.Serializable]
    public class HierarchyData {
        public List<(string, SlotInfo)> AllSlots;
        public List<(string, SlotInfo)> SlotsWithMeshRenderer;
    }

    [System.Serializable]
    public class ValueWithId<T> {
        [JsonProperty("value")]
        public T Value;

        [JsonProperty("id")]
        public string Id;
    }

    [System.Serializable]
    public class ResoLinkResponse {
        [JsonProperty("data")]
        public SlotData Data;
    }

    [System.Serializable]
    public class ResoLinkComponentResponse {
        [JsonProperty("data")]
        public ComponentData Data;
    }
}