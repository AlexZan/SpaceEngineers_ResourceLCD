// Script: SE_OreIngotPanel.cs
// Purpose: Ore and ingot totals on two LCDs, sorted ascending, aligned columns. Tank logic removed.
// Notes: Uses NL char to avoid wrapped string literals. Monospace font recommended on panels.

const string ORE_PANEL_TAG = "[ResLCD Ore]";
const string INGOT_PANEL_TAG = "[ResLCD Ingot]";
const float ICE_THRESHOLD = 42000.0f;
const char NL = '\n';
const int RESCAN_INTERVAL = 10;

List<IMyTextPanel> cachedPanels = new List<IMyTextPanel>();
List<IMyTextSurface> orePanels = new List<IMyTextSurface>();
List<IMyTextSurface> ingotPanels = new List<IMyTextSurface>();
List<IMyTerminalBlock> cachedBlocks = new List<IMyTerminalBlock>();
List<IMyInventory> cachedInventories = new List<IMyInventory>();
int cyclesSinceRescan = 0;
bool hasScanned = false;

// Keys to always show
static readonly string[] ORE_KEYS = new string[]
{
    "Iron Ore","Nickel Ore","Cobalt Ore","Silicon Ore","Magnesium Ore",
    "Silver Ore","Gold Ore","Platinum Ore","Uranium Ore","Stone","Organic","Scrap Metal","Ice"
};

static readonly string[] INGOT_KEYS = new string[]
{
    "Cobalt Ingot","Gold Ingot","Iron Ingot","Magnesium Pow","Nickel Ingot",
    "Platinum Ingot","Silicon Wafer","Silver Ingot","Uranium Ingot"
};

void Rescan()
{
    cachedPanels.Clear();
    orePanels.Clear();
    ingotPanels.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(cachedPanels, filterPanels);

    for (int i = 0; i < cachedPanels.Count; i++)
    {
        var panel = cachedPanels[i];
        var surface = GetPrimarySurface(panel);
        if (surface == null) continue;
        bool added = false;
        if (PanelHasTag(panel, surface, ORE_PANEL_TAG))
        {
            orePanels.Add(surface);
            added = true;
        }
        if (!added && PanelHasTag(panel, surface, INGOT_PANEL_TAG))
        {
            ingotPanels.Add(surface);
        }
    }

    cachedBlocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(cachedBlocks, filterInventories);

    cachedInventories.Clear();
    for (int i = 0; i < cachedBlocks.Count; i++)
    {
        cachedInventories.AddRange(EnumerateInventories(cachedBlocks[i]));
    }

    cyclesSinceRescan = 0;
    hasScanned = true;
}

void Main(string argument)
{
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    if (!hasScanned || argument == "rescan") Rescan();
    else if (++cyclesSinceRescan >= RESCAN_INTERVAL) Rescan();

    string ERR_TXT = "";

    // Panels
    if (cachedPanels.Count == 0) ERR_TXT += "no LCD Panel blocks found" + NL;
    else if (orePanels.Count == 0 && ingotPanels.Count == 0)
        ERR_TXT += "no LCD panels tagged with " + ORE_PANEL_TAG + " or " + INGOT_PANEL_TAG + " found" + NL;

    // Inventories
    if (cachedBlocks.Count == 0) ERR_TXT += "No blocks with inventories found" + NL;

    var inventories = cachedInventories;

    if (ERR_TXT != "")
    {
        Echo("Script Errors:" + NL + ERR_TXT + "(make sure block ownership is set correctly)");
        return;
    }
    else Echo("");

    // Tally
    IDictionary<string, float> ore = new Dictionary<string, float>();
    IDictionary<string, float> ing = new Dictionary<string, float>();
    Seed(ore, ORE_KEYS); Seed(ing, INGOT_KEYS);

    Echo(inventories.Count + " inventories with ore or ingots");
    var buffer = new List<MyInventoryItem>();
    for (int i = 0; i < inventories.Count; i++)
    {
        buffer.Clear();
        inventories[i].GetItems(buffer, filterItemsForOreAndIngots);
        for (int j = 0; j < buffer.Count; j++)
        {
            var it = buffer[j];
            string typeId = it.Type.TypeId;
            string name = decodeItemName(it);
            float amt = getItemAmountAsFloat(it);
            float prev;
            if (typeId.EndsWith("Ore")) { if (ore.TryGetValue(name, out prev)) ore[name] = prev + amt; }
            else if (typeId.EndsWith("Ingot")) { if (ing.TryGetValue(name, out prev)) ing[name] = prev + amt; }
        }
    }

    // Build panel texts
    bool oreHasLowIce;
    string oreText = BuildPanelText(ore, true, out oreHasLowIce);
    bool ingHasLowIce;
    string ingText = BuildPanelText(ing, false, out ingHasLowIce);

    string oreDisplay = oreText;
    string ingDisplay = ingText;
    if (orePanels.Count > 0 && ingotPanels.Count == 0)
        oreDisplay = "ORE" + NL + oreText + NL + NL + "INGOTS" + NL + ingText;
    if (ingotPanels.Count > 0 && orePanels.Count == 0)
        ingDisplay = "ORE" + NL + oreText + NL + NL + ingText;

    bool highlightIngotPanels = orePanels.Count == 0 && ingotPanels.Count > 0 && oreHasLowIce;

    WritePanelText(orePanels, oreDisplay, oreHasLowIce);
    WritePanelText(ingotPanels, ingDisplay, highlightIngotPanels);
}

// Helpers
void Seed(IDictionary<string,float> dict, string[] keys)
{
    for (int i = 0; i < keys.Length; i++) dict[keys[i]] = 0f;
}

void WritePanelText(List<IMyTextSurface> panels, string text, bool highlightLowIce)
{
    for (int i = 0; i < panels.Count; i++)
    {
        var panel = panels[i];
        panel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
        panel.Font = "Monospace";
        panel.FontSize = 1f;
        panel.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
        panel.FontColor = highlightLowIce ? new Color(255, 0, 0) : new Color(255, 255, 255);
        panel.WriteText(text, false);
    }
}

string BuildPanelText(IDictionary<string,float> dict, bool isOre, out bool highlightLowIce)
{
    var list = new List<KeyValuePair<string, float>>(dict);
    list.Sort((a, b) => a.Value.CompareTo(b.Value));

    int labelWidth = 0;
    for (int i = 0; i < list.Count; i++)
    {
        int w = list[i].Key.Length + 2; // ": "
        if (w > labelWidth) labelWidth = w;
    }

    string text = "";
    highlightLowIce = false;
    for (int i = 0; i < list.Count; i++)
    {
        var kvp = list[i];
        bool isIce = isOre && kvp.Key == "Ice";
        string qty = FormatQty(kvp.Value, isIce);
        if (isIce && kvp.Value < ICE_THRESHOLD) highlightLowIce = true;
        string label = kvp.Key + ": ";
        if (text.Length > 0) text += NL;
        text += PadRight(label, labelWidth) + qty;
    }
    return text;
}

string FormatQty(float v, bool forceInteger)
{
    if (forceInteger) return ((float)Math.Round((double)v, 0)).ToString();
    if (v < 5000f) return ((float)Math.Round((double)v, 0)).ToString();
    return ((float)Math.Round((double)(v / 1000f), 0)).ToString() + "k";
}

float getItemAmountAsFloat(MyInventoryItem item)
{
    float count = 0f; float.TryParse("" + item.Amount, out count); return count;
}

string PadRight(string input, int num)
{
    if (input.Length < num) for (int i = input.Length; i < num; i++) input += " ";
    return input;
}

IEnumerable<IMyInventory> EnumerateInventories(IMyTerminalBlock block)
{
    int inventoryCount = block.InventoryCount;
    for (int i = 0; i < inventoryCount; i++)
    {
        yield return block.GetInventory(i);
    }
}

bool filterPanels(IMyTextPanel panel) { return panel.CubeGrid == Me.CubeGrid; }

bool PanelHasTag(IMyTextPanel panel, IMyTextSurface surface, string tag)
{
    if (surface == null) return false;
    string customData = panel.CustomData;
    if (ContainsTag(customData, tag)) return true;

    string surfaceText = surface.GetText();
    if (ContainsTag(surfaceText, tag))
    {
        PersistTag(panel, tag, customData);
        return true;
    }
    return false;
}

void PersistTag(IMyTextPanel panel, string tag, string existingCustomData)
{
    if (ContainsTag(existingCustomData, tag)) return;
    string data = string.IsNullOrEmpty(existingCustomData) ? tag : existingCustomData + NL + tag;
    panel.CustomData = data;
}

bool ContainsTag(string source, string tag)
{
    return !string.IsNullOrEmpty(source) && source.IndexOf(tag, System.StringComparison.OrdinalIgnoreCase) >= 0;
}

IMyTextSurface GetPrimarySurface(IMyTextPanel panel)
{
    return panel as IMyTextSurface;
}

bool filterInventories(IMyTerminalBlock block)
{
    if (block.CubeGrid != Me.CubeGrid || !block.HasInventory) return false;
    List<MyInventoryItem> items = new List<MyInventoryItem>();
    int allItemsCount = 0;
    block.GetInventory(0).GetItems(items); allItemsCount += items.Count;
    if (block.InventoryCount == 2) { items.Clear(); block.GetInventory(1).GetItems(items); allItemsCount += items.Count; }
    if (allItemsCount == 0) return false; return true;
}

private bool filterItemsForOreAndIngots(MyInventoryItem item)
{
    return item.Type.TypeId.EndsWith("Ore") || item.Type.TypeId.EndsWith("Ingot");
}

// Minimal Ore/Ingot display names
string decodeItemName(MyInventoryItem item)
{
    string typeId = item.Type.TypeId;
    string sub = item.Type.SubtypeId;

    if (typeId.EndsWith("Ore"))
    {
        if (sub.Equals("Scrap")) return "Scrap Metal";
        if (sub.Equals("Stone") || sub.Equals("Ice") || sub.Equals("Organic")) return sub;
        return sub + " Ore";
    }

    if (typeId.EndsWith("Ingot"))
    {
        if (sub.Equals("Scrap")) return "Old Scrap Metal";
        if (sub.Equals("Stone")) return "Gravel";
        if (sub.Equals("Magnesium")) return "Magnesium Pow";
        if (sub.Equals("Silicon")) return "Silicon Wafer";
        return sub + " Ingot";
    }

    return sub;
}
