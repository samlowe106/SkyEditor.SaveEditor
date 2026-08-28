using System.Globalization;
using System.Reflection;
using SkyEditor.SaveEditor;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    return args[0] switch
    {
        "info" => RunInfo(args[1..]),
        "mark-boss" => RunMarkBoss(args[1..]),
        "unlock-friend-area" => RunUnlockFriendArea(args[1..]),
        "add-money" => RunAddMoney(args[1..]),
        "add-item" => RunAddItem(args[1..]),
        "-h" or "--help" or "help" => Help(),
        _ => UnknownCommand(args[0]),
    };
}
catch (CliException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int Help()
{
    PrintUsage();
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        ske - Red/Blue Rescue Team save editor CLI

        Usage:
          ske info <save>
          ske mark-boss <save> <boss-name> [--level N] [--out path]
          ske unlock-friend-area <save> <friend-area-name> [--out path]
          ske add-money <save> <amount> [--stored] [--out path]
          ske add-item <save> <item-name-or-id> <quantity> [--out path]

        By default, edits are written back to the input file, and the
        pre-edit bytes are saved alongside it as "<save>.bak" first.
        Pass --out to write to a different file and leave the input alone.

        Run "ske info <save>" with no other arguments to list valid boss
        names and friend area names for this save.
        """);
}

static RBSave LoadSave(string path)
{
    if (!File.Exists(path))
    {
        throw new CliException($"Save file not found: {path}");
    }

    var save = RBSave.FromFile(File.ReadAllBytes(path));
    if (save == null)
    {
        throw new CliException($"'{path}' looks like a SharkPort (.sps) export, but no valid save payload could be found inside it.");
    }

    return save;
}

static void WriteSave(RBSave save, string inputPath, string? outPath)
{
    if (outPath != null)
    {
        File.WriteAllBytes(outPath, save.ToByteArray());
        Console.WriteLine($"Wrote {outPath}");
        return;
    }

    File.Copy(inputPath, inputPath + ".bak", overwrite: true);
    File.WriteAllBytes(inputPath, save.ToByteArray());
    Console.WriteLine($"Wrote {inputPath} (previous contents backed up to {inputPath}.bak)");
}

static (List<string> positional, string? outPath, Dictionary<string, string> options) ParseArgs(string[] args, params string[] flagOptions)
{
    var positional = new List<string>();
    string? outPath = null;
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--out")
        {
            outPath = args[++i];
        }
        else if (args[i].StartsWith("--") && Array.IndexOf(flagOptions, args[i][2..]) >= 0)
        {
            options[args[i][2..]] = args[++i];
        }
        else
        {
            positional.Add(args[i]);
        }
    }

    return (positional, outPath, options);
}

static Dictionary<string, int> GetBossNames()
{
    var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var field in typeof(RBBossEncounters).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (field.FieldType == typeof(int))
        {
            result[field.Name] = (int)field.GetValue(null)!;
        }
    }
    return result;
}

static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

static int RunInfo(string[] args)
{
    var (positional, _, _) = ParseArgs(args);
    if (positional.Count < 1)
    {
        throw new CliException("Usage: ske info <save>");
    }

    var save = LoadSave(positional[0]);

    Console.WriteLine($"Team name: {save.TeamName}");
    Console.WriteLine($"Held money: {save.HeldMoney}");
    Console.WriteLine($"Stored money: {save.StoredMoney}");
    Console.WriteLine($"Rescue team points: {save.RescueTeamPoints}");
    Console.WriteLine($"Primary checksum valid: {save.IsPrimaryChecksumValid()}");
    Console.WriteLine($"Secondary checksum valid: {save.IsSecondaryChecksumValid()}");

    Console.WriteLine();
    Console.WriteLine($"Recruited roster ({save.StoredPokemon.Count} Pokemon):");
    foreach (var pkm in save.StoredPokemon.OrderBy(p => p.SlotIndex))
    {
        var speciesName = Lists.RBPokemon.TryGetValue(pkm.ID, out var n) ? n : $"#{pkm.ID}";
        var held = pkm.HeldItemId != 0
            ? $"  holds {(Lists.RBItems.TryGetValue(pkm.HeldItemId, out var itemName) ? itemName : $"item #{pkm.HeldItemId}")}{(pkm.HeldItemQuantity > 0 ? $" x{pkm.HeldItemQuantity}" : "")}"
            : "";
        Console.WriteLine($"  [slot {pkm.SlotIndex,3}] {speciesName,-16} Lv.{pkm.Level} \"{pkm.Name}\"{held}");
    }

    Console.WriteLine();
    Console.WriteLine($"Toolbox ({save.HeldItems.Count}/20):");
    foreach (var item in save.HeldItems)
    {
        var itemName = Lists.RBItems.TryGetValue(item.ID, out var tn) ? tn : $"#{item.ID}";
        Console.WriteLine($"  {itemName}{(item.Parameter > 0 ? $" x{item.Parameter}" : "")}");
    }
    if (save.HeldItems.Count == 0) Console.WriteLine("  (empty)");

    Console.WriteLine();
    Console.WriteLine("Story bosses:");
    foreach (var (name, id) in GetBossNames().OrderBy(kv => kv.Value))
    {
        var recruited = save.StoredPokemon.Exists(p => p.ID == id);
        var hasFlag = RBBossEncounters.CompleteFlagsByBoss.TryGetValue(id, out var flag);
        var flagState = hasFlag ? (save.ExclusivePokemonData.GetCutsceneFlag(flag) ? "complete flag set" : "complete flag NOT set") : "no complete flag needed";
        var recruitableState = recruited ? "" : $" recruitable={save.CanCurrentlyRecruit(id)}";
        Console.WriteLine($"  {name,-12} recruited={recruited,-5}{recruitableState} {flagState}");
    }

    Console.WriteLine();
    Console.WriteLine("Story flags:");
    RBStoryPhase? currentPhase = null;
    foreach (var info in RBStoryFlags.All)
    {
        if (info.Phase != currentPhase)
        {
            currentPhase = info.Phase;
            Console.WriteLine($"  {currentPhase switch { RBStoryPhase.MainStory => "Main story", RBStoryPhase.Postgame => "Postgame", _ => "Transient scratch (not story progress)" }}:");
        }
        var set = save.ExclusivePokemonData.GetCutsceneFlag(info.Flag);
        Console.WriteLine($"    [{(set ? 'x' : ' ')}] {info.Flag,-22} {info.Description}");
    }

    Console.WriteLine();
    Console.WriteLine("Wonder Mail:");
    void PrintMailSection(string label, System.Collections.Generic.List<RBWonderMail> slots)
    {
        Console.WriteLine($"  {label}:");
        var any = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty) continue;
            any = true;
            var password = slots[i].IsWonderMail ? $"  [{RBWonderMailPassword.FormatForDisplay(RBWonderMailPassword.Encode(slots[i]))}]" : "";
            Console.WriteLine($"    [{i}] {slots[i].GetMissionSummary()} -- {slots[i].GetRewardSummary()}{password}");
        }
        if (!any) Console.WriteLine("    (none)");
    }
    PrintMailSection("Accepted jobs", save.MailData.JobSlots);
    PrintMailSection("Pelipper board", save.MailData.PelipperBoardJobs);
    PrintMailSection("Mailbox", save.MailData.MailboxSlots);
    Console.WriteLine("  Used Wonder Mail history (passwords the game will reject as already used; oldest is evicted when a 17th job completes):");
    var anyUsed = false;
    for (int i = 0; i < save.MailData.UsedMailHistory.Count; i++)
    {
        if (save.MailData.UsedMailHistory[i].IsEmpty) continue;
        anyUsed = true;
        Console.WriteLine($"    [{i,2}] {save.MailData.UsedMailHistory[i].GetSummary()}");
    }
    if (!anyUsed) Console.WriteLine("    (none)");

    Console.WriteLine();
    Console.WriteLine("Friend areas:");
    foreach (RBFriendArea area in Enum.GetValues<RBFriendArea>())
    {
        if (area == RBFriendArea.None) continue;
        Console.WriteLine($"  {area,-24} unlocked={save.FriendAreasUnlocked[(int)area]}");
    }

    return 0;
}

static int RunMarkBoss(string[] args)
{
    var (positional, outPath, options) = ParseArgs(args, "level", "name");
    if (positional.Count < 2)
    {
        throw new CliException("Usage: ske mark-boss <save> <boss-name> [--level N] [--out path]");
    }

    var savePath = positional[0];
    var bossNames = GetBossNames();
    if (!bossNames.TryGetValue(positional[1], out var bossId))
    {
        throw new CliException($"Unknown boss '{positional[1]}'. Valid names: {string.Join(", ", bossNames.Keys.OrderBy(k => k))}");
    }

    var level = options.TryGetValue("level", out var levelStr) ? int.Parse(levelStr, CultureInfo.InvariantCulture) : 30;
    var name = options.TryGetValue("name", out var nameOverride) ? nameOverride
        : Lists.RBPokemon.TryGetValue(bossId, out var speciesName) ? speciesName : "Boss";

    var save = LoadSave(savePath);

    var pokemon = new RBStoredPokemon
    {
        ID = bossId,
        Name = name,
        Level = Math.Clamp(level, 1, 100),
        IQ = 1,
        HP = Math.Clamp(level * 10, 1, 999),
        Attack = Math.Clamp(level * 2, 1, 255),
        SpAttack = Math.Clamp(level * 2, 1, 255),
        Defense = Math.Clamp(level * 2, 1, 255),
        SpDefense = Math.Clamp(level * 2, 1, 255),
        Exp = 0,
        Attack1 = new RBAttack(),
        Attack2 = new RBAttack(),
        Attack3 = new RBAttack(),
        Attack4 = new RBAttack(),
    };

    var added = save.MarkBossRecruited(bossId, pokemon);
    var hasFlag = RBBossEncounters.CompleteFlagsByBoss.TryGetValue(bossId, out var flag);

    if (added)
    {
        Console.WriteLine($"Added {name} (species #{bossId}, Lv.{pokemon.Level}) to the recruited roster.");
    }
    else
    {
        Console.WriteLine($"{name} was already in the recruited roster; roster unchanged.");
    }
    Console.WriteLine(hasFlag
        ? $"Set cutscene flag {flag} so the story encounter won't replay."
        : "This boss has no cutscene complete flag; the roster entry alone is enough (per the decomp, its encounter script only checks HasRecruitedMon()).");

    WriteSave(save, savePath, outPath);
    return 0;
}

static int RunUnlockFriendArea(string[] args)
{
    var (positional, outPath, _) = ParseArgs(args);
    if (positional.Count < 2)
    {
        throw new CliException("Usage: ske unlock-friend-area <save> <friend-area-name> [--out path]");
    }

    var savePath = positional[0];
    var target = Normalize(positional[1]);
    var match = Enum.GetValues<RBFriendArea>().Cast<RBFriendArea>()
        .FirstOrDefault(a => Normalize(a.ToString()) == target, (RBFriendArea)(-1));

    if ((int)match < 0)
    {
        var names = string.Join(", ", Enum.GetNames<RBFriendArea>().Where(n => n != nameof(RBFriendArea.None)));
        throw new CliException($"Unknown friend area '{positional[1]}'. Valid names: {names}");
    }

    var save = LoadSave(savePath);
    var changed = save.UnlockFriendArea(match);

    Console.WriteLine(changed
        ? $"Unlocked friend area {match}."
        : $"Friend area {match} was already unlocked; save unchanged.");

    WriteSave(save, savePath, outPath);
    return 0;
}

static int RunAddMoney(string[] args)
{
    var (positional, outPath, _) = ParseArgs(args);
    var stored = args.Contains("--stored");
    positional.RemoveAll(p => p == "--stored");
    if (positional.Count < 2)
    {
        throw new CliException("Usage: ske add-money <save> <amount> [--stored] [--out path]");
    }

    var savePath = positional[0];
    var amount = int.Parse(positional[1], CultureInfo.InvariantCulture);

    var save = LoadSave(savePath);
    if (stored)
    {
        save.StoredMoney += amount;
        Console.WriteLine($"Stored money: {save.StoredMoney - amount} -> {save.StoredMoney}");
    }
    else
    {
        save.HeldMoney += amount;
        Console.WriteLine($"Held money: {save.HeldMoney - amount} -> {save.HeldMoney}");
    }

    WriteSave(save, savePath, outPath);
    return 0;
}

static int RunAddItem(string[] args)
{
    var (positional, outPath, _) = ParseArgs(args);
    if (positional.Count < 3)
    {
        throw new CliException("Usage: ske add-item <save> <item-name-or-id> <quantity> [--out path]");
    }

    var savePath = positional[0];
    var itemArg = positional[1];
    var quantity = int.Parse(positional[2], CultureInfo.InvariantCulture);

    int itemId;
    if (int.TryParse(itemArg, out var parsedId))
    {
        itemId = parsedId;
    }
    else
    {
        var match = Lists.RBItems.FirstOrDefault(kv => string.Equals(kv.Value, itemArg, StringComparison.OrdinalIgnoreCase));
        if (match.Value == null)
        {
            throw new CliException($"Unknown item '{itemArg}'. Pass a numeric item ID, or an exact item name.");
        }
        itemId = match.Key;
    }

    if (!Lists.RBItems.TryGetValue(itemId, out var itemName))
    {
        throw new CliException($"Item ID {itemId} is not a recognized item.");
    }

    var save = LoadSave(savePath);
    save.StoredItems.Add(new RBStoredItem(itemId, quantity));
    Console.WriteLine($"Added {quantity}x {itemName} to storage.");

    WriteSave(save, savePath, outPath);
    return 0;
}

sealed class CliException : Exception
{
    public CliException(string message) : base(message) { }
}
