// Script: SE_OreIngotPanel.cs
// Purpose: Ore and ingot totals on two LCDs, sorted ascending, aligned columns. Tank logic removed.
// Notes: Uses NL char to avoid wrapped string literals. Monospace font recommended on panels.

const string DEFAULT_ORE_PANEL_TAG = "[ResLCD Ore]";
const string DEFAULT_INGOT_PANEL_TAG = "[ResLCD Ingot]";
const char NL = '\n';
const int DEFAULT_RESCAN_INTERVAL = 10;

string orePanelTag = DEFAULT_ORE_PANEL_TAG;
string ingotPanelTag = DEFAULT_INGOT_PANEL_TAG;
int rescanInterval = DEFAULT_RESCAN_INTERVAL;

List<IMyTextPanel> cachedPanels = new List<IMyTextPanel>();
List<IMyTextSurface> orePanels = new List<IMyTextSurface>();
List<IMyTextSurface> ingotPanels = new List<IMyTextSurface>();
List<IMyTerminalBlock> cachedBlocks = new List<IMyTerminalBlock>();
List<IMyInventory> cachedInventories = new List<IMyInventory>();
List<MyInventoryItem> inventoryBuffer = new List<MyInventoryItem>();
int cyclesSinceRescan = 0;
bool hasScanned = false;

// Keys to always show
static readonly string[] ORE_KEYS = new string[]
{
    "Iron Ore","Nickel Ore","Cobalt Ore","Silicon Ore","Magnesium Ore",
    "Silver Ore","Gold Ore","Platinum Ore","Uranium Ore","Stone","Organic","Scrap Metal","Ice"
};

static readonly Dictionary<string, string> ORE_EXCEPTIONS = new Dictionary<string, string>()
{
    {"Scrap", "Scrap Metal"},
    {"Stone", "Stone"},
    {"Ice", "Ice"},
    {"Organic", "Organic"},
};

static readonly string[] INGOT_KEYS = new string[]
{
    "Cobalt Ingot","Gold Ingot","Iron Ingot","Magnesium Pow","Nickel Ingot",
    "Platinum Ingot","Silicon Wafer","Silver Ingot","Uranium Ingot"
};

static readonly Dictionary<string, string> INGOT_EXCEPTIONS = new Dictionary<string, string>()
{
    {"Scrap", "Old Scrap Metal"},
    {"Stone", "Gravel"},
    {"Magnesium", "Magnesium Pow"},
    {"Silicon", "Silicon Wafer"},
};

bool LoadConfig()
{
    string previousOreTag = orePanelTag;
    string previousIngotTag = ingotPanelTag;
    int previousRescanInterval = rescanInterval;

    orePanelTag = DEFAULT_ORE_PANEL_TAG;
    ingotPanelTag = DEFAULT_INGOT_PANEL_TAG;
    rescanInterval = DEFAULT_RESCAN_INTERVAL;

    string data = Me.CustomData;
    if (!string.IsNullOrWhiteSpace(data))
    {
        string[] lines = data.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//") || line.StartsWith("#") || line.StartsWith(";")) continue;

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0) continue;
            }

            string key = line.Substring(0, separatorIndex).Trim();
            string value = line.Substring(separatorIndex + 1).Trim();
            if (key.Length == 0) continue;

            value = TrimMatchingQuotes(value).Trim();

            if (string.Equals(key, "RESCAN_INTERVAL", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed) && parsed > 0)
                {
                    rescanInterval = parsed;
                }
            }
            else if (string.Equals(key, "ORE_PANEL_TAG", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    orePanelTag = value;
                }
            }
            else if (string.Equals(key, "INGOT_PANEL_TAG", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    ingotPanelTag = value;
                }
            }
        }
    }

    if (rescanInterval <= 0)
    {
        rescanInterval = DEFAULT_RESCAN_INTERVAL;
    }

    bool tagsChanged = !string.Equals(previousOreTag, orePanelTag, StringComparison.Ordinal);
    if (!tagsChanged)
    {
        tagsChanged = !string.Equals(previousIngotTag, ingotPanelTag, StringComparison.Ordinal);
    }

    if (previousRescanInterval != rescanInterval && cyclesSinceRescan > rescanInterval)
    {
        cyclesSinceRescan = rescanInterval;
    }

    return tagsChanged;
}

string TrimMatchingQuotes(string value)
{
    if (value.Length >= 2)
    {
        char first = value[0];
        char last = value[value.Length - 1];
        if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
        {
            return value.Substring(1, value.Length - 2);
        }
    }
    return value;
}

void Rescan()
{
    cachedPanels.Clear();
    orePanels.Clear();
    ingotPanels.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(cachedPanels, FilterPanels);

    for (int i = 0; i < cachedPanels.Count; i++)
    {
        var panel = cachedPanels[i];
        var surface = GetPrimarySurface(panel);
        if (surface == null) continue;
        bool added = false;
        if (PanelHasTag(panel, surface, orePanelTag))
        {
            orePanels.Add(surface);
            added = true;
        }
        if (!added && PanelHasTag(panel, surface, ingotPanelTag))
        {
            ingotPanels.Add(surface);
        }
    }

    cachedBlocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(cachedBlocks, FilterInventories);

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

    bool configChanged = LoadConfig();

    if (!hasScanned || argument == "rescan" || configChanged) Rescan();
    else if (++cyclesSinceRescan >= rescanInterval) Rescan();

    string ERR_TXT = "";

    // Panels
    if (cachedPanels.Count == 0) ERR_TXT += "no LCD Panel blocks found" + NL;
    else if (orePanels.Count == 0 && ingotPanels.Count == 0)
        ERR_TXT += "no LCD panels tagged with " + orePanelTag + " or " + ingotPanelTag + " found" + NL;

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
            string name = DecodeItemName(it);
            float amt = GetItemAmountAsFloat(it);
            float prev;
            if (typeId.EndsWith("Ore")) { if (ore.TryGetValue(name, out prev)) ore[name] = prev + amt; }
            else if (typeId.EndsWith("Ingot")) { if (ing.TryGetValue(name, out prev)) ing[name] = prev + amt; }
        }
    }

    // Build panel texts
    string oreText = BuildPanelText(ore, true);
    string ingText = BuildPanelText(ing, false);

    string oreDisplay = oreText;
    string ingDisplay = ingText;
    if (orePanels.Count > 0 && ingotPanels.Count == 0)
        oreDisplay = "ORE" + NL + oreText + NL + NL + "INGOTS" + NL + ingText;
    if (ingotPanels.Count > 0 && orePanels.Count == 0)
        ingDisplay = "ORE" + NL + oreText + NL + NL + ingText;

    WritePanelText(orePanels, oreDisplay);
    WritePanelText(ingotPanels, ingDisplay);
}

// Helpers
void Seed(IDictionary<string,float> dict, string[] keys)
{
    for (int i = 0; i < keys.Length; i++) dict[keys[i]] = 0f;
}

void WritePanelText(List<IMyTextSurface> panels, string text)
{
    for (int i = 0; i < panels.Count; i++)
    {
        var panel = panels[i];
        panel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
        panel.Font = "Monospace";
        panel.FontSize = 1f;
        panel.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
        panel.FontColor = new Color(255, 255, 255);
        panel.WriteText(text, false);
    }
}

string BuildPanelText(IDictionary<string,float> dict, bool isOre)
{
    var list = new List<KeyValuePair<string, float>>(dict);
    list.Sort((a, b) => a.Value.CompareTo(b.Value));

    int labelWidth = 0;
    for (int i = 0; i < list.Count; i++)
    {
        int w = list[i].Key.Length + 2; // ": "
        if (w > labelWidth) labelWidth = w;
    }

    var builder = new System.Text.StringBuilder();
    for (int i = 0; i < list.Count; i++)
    {
        var kvp = list[i];
        bool isIce = isOre && kvp.Key == "Ice";
        string qty = FormatQty(kvp.Value, isIce);
        string label = kvp.Key + ": ";
        if (builder.Length > 0) builder.AppendLine();
        builder.Append(label.PadRight(labelWidth));
        builder.Append(qty);
    }
    return builder.ToString();
}

// Examples of the formatting behaviour:
// FormatQty(950f, false)   => "950"
// FormatQty(1500f, false)  => "1.5k"
// FormatQty(1250000f, false) => "1.25M"
// FormatQty(2500000000f, false) => "2.5B"
// FormatQty(1500f, true)   => "1500" (forceInteger disables scaling)
string FormatQty(float v, bool forceInteger)
{
    if (forceInteger) return Math.Round((double)v, 0).ToString();

    double absValue = Math.Abs((double)v);
    if (absValue < 1000d) return Math.Round((double)v, 0).ToString();

    string[] suffixes = new[] { "k", "M", "B" };
    double scaled = v;
    int suffixIndex = -1;

    while (suffixIndex + 1 < suffixes.Length && Math.Abs(scaled) >= 1000d)
    {
        scaled /= 1000d;
        suffixIndex++;
    }

    double absScaled = Math.Abs(scaled);
    int decimals = absScaled >= 100d ? 0 : (absScaled >= 10d ? 1 : 2);
    double rounded = Math.Round(scaled, decimals);

    if (Math.Abs(rounded) >= 1000d && suffixIndex + 1 < suffixes.Length)
    {
        scaled = rounded / 1000d;
        suffixIndex++;
        absScaled = Math.Abs(scaled);
        decimals = absScaled >= 100d ? 0 : (absScaled >= 10d ? 1 : 2);
        rounded = Math.Round(scaled, decimals);
    }

    string format = decimals == 0 ? "0" : "0." + new string('#', decimals);
    return rounded.ToString(format) + suffixes[suffixIndex];
}

float GetItemAmountAsFloat(MyInventoryItem item)
{
    return (float)item.Amount;
}

IEnumerable<IMyInventory> EnumerateInventories(IMyTerminalBlock block)
{
    int inventoryCount = block.InventoryCount;
    for (int i = 0; i < inventoryCount; i++)
    {
        yield return block.GetInventory(i);
    }
}

bool FilterPanels(IMyTextPanel panel) { return panel.CubeGrid == Me.CubeGrid; }

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

bool FilterInventories(IMyTerminalBlock block)
{
    if (block.CubeGrid != Me.CubeGrid || !block.HasInventory) return false;
    int allItemsCount = 0;
    for (int i = 0; i < block.InventoryCount; i++)
    {
        inventoryBuffer.Clear();
        block.GetInventory(i).GetItems(inventoryBuffer, filterItemsForOreAndIngots);
        allItemsCount += inventoryBuffer.Count;
        if (allItemsCount > 0) break;
    }
    return allItemsCount > 0;
}

private bool filterItemsForOreAndIngots(MyInventoryItem item)
{
    return item.Type.TypeId.EndsWith("Ore") || item.Type.TypeId.EndsWith("Ingot");
}

// Minimal Ore/Ingot display names
string DecodeItemName(MyInventoryItem item)
{
    string typeId = item.Type.TypeId;
    string sub = item.Type.SubtypeId;

    if (typeId.EndsWith("Ore"))
    {
        string name;
        if (ORE_EXCEPTIONS.TryGetValue(sub, out name)) return name;
        return sub + " Ore";
    }

    if (typeId.EndsWith("Ingot"))
    {
        string name;
        if (INGOT_EXCEPTIONS.TryGetValue(sub, out name)) return name;
        return sub + " Ingot";
    }

    return sub;
}
