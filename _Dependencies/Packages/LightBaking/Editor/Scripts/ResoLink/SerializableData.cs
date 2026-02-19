using System.Collections.Generic;

[System.Serializable]
public class SlotInfo {
    public ResoniteLink.SlotData Data;
    public string[] Path;
    public string MeshId;
}

[System.Serializable]
public class HierarchyData {
    public List<(string, SlotInfo)> AllSlots;
    public List<(string, SlotInfo)> SlotsWithMeshRenderer;
}
