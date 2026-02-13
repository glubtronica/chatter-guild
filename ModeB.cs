// Program.cs
// Chatter's Guild - Mode B (Core Conversation + Rotating Modifiers)
// Modifiers included:
//  1) Question Economy (limited question tokens, earn more via good turns)
//  2) Mirror Mode (reply must be fewer / same / more words than previous turn)
//  3) Role Lock Challenge (only Questions / only Statements / only Reflections / only Examples)
//
// Console, offline, no server, no internet.
// Strict turn-taking (2 players). Type "end" to finish.

using System;
using System.Collections.Generic;
using System.Linq;

public enum ModifierType { QuestionEconomy, MirrorMode, RoleLock }
public enum MirrorRule { FewerWords, SameWords, MoreWords }
public enum RoleLockRule { OnlyQuestions, OnlyStatements, OnlyReflections, OnlyExamples }

public struct BehaviorVector { public int OC, IN, IQ, RF; }

public sealed class TurnScore
{
    public BehaviorVector B;
    public int Raw;
    public double Norm;
    public int IP;
    public int BonusIP;     // modifier compliance bonuses
    public int PenaltyIP;   // modifier compliance penalties
    public bool CompliedAll;
}

public sealed class Player
{
    public string Name;
    public int TotalIP = 0;

    // Question Economy
    public int QuestionTokens = 5;

    public Player(string name) { Name = name; }
}

public static class Program
{
    // ---- Core settings ----
    const int TurnsPerMatch = 20;          // total turns (both players)
    const int BaseMinIP = 0;
    const int BaseMaxIP = 30;

    // ---- Modifier settings ----
    const int QuestionTokenCost = 1;       // per question asked
    const int QuestionTokenEarnThresholdIP = 12; // if you score >= this IP on a turn, earn 1 token
    const int QuestionTokenEarnCapPerTurn = 1;

    const int ComplianceBonusIP = 4;       // per-turn bonus if you comply with all active rules
    const int CompliancePenaltyIP = 6;     // per-turn penalty if you violate any active rule

    // ---- Scoring normalization across run (per "implicit role") ----
    // We'll normalize by turn type category: "Question", "Statement", "Reflection", "Example"
    enum TurnCategory { Question, Statement, Reflection, Example }
    static readonly Dictionary<TurnCategory, int> Count = new();
    static readonly Dictionary<TurnCategory, int> SumRaw = new();

    static readonly Random Rng = new();

    public static void Main()
    {
        Console.WriteLine("=== Chatter's Guild: Mode B (Core + Rotating Modifiers) ===");
        Console.WriteLine("2 players • strict turn-taking • offline");
        Console.WriteLine("Type 'end' at any prompt to stop.\n");

        var p1 = new Player(ReadNonEmpty("Player 1 name", "U1"));
        var p2 = new Player(ReadNonEmpty("Player 2 name", "U2"));

        Console.WriteLine("\nChoose how modifiers rotate:");
        Console.WriteLine("  1) Rotate EVERY TURN (unpredictable)");
        Console.WriteLine("  2) Rotate EVERY 4 TURNS (mini-rounds)");
        int rotateMode = ReadInt("Enter 1 or 2", 1, 2, 2);
        int rotationPeriod = rotateMode == 1 ? 1 : 4;

        // Active modifier state (changes on rotation)
        var active = new ActiveModifiers();

        // Conversation memory
        string lastMsg = ""; // last message globally (for Mirror Mode)
        var recentTokens = new HashSet<string>();
        int turnIndex = 0;

        // Start with a random modifier set
        active.RotateToRandomSet();

        Console.WriteLine("\n--- MATCH START ---");
        PrintModifiers(active);

        Player[] order = { p1, p2 };
        int current = 0;

        while (turnIndex < TurnsPerMatch)
        {
            if (turnIndex > 0 && (turnIndex % rotationPeriod == 0))
            {
                Console.WriteLine("\n🎵 (imagined) music shifts… then stops.");
                active.RotateToRandomSet();
                PrintModifiers(active);
            }

            var actor = order[current];
            var partner = order[1 - current];

            Console.WriteLine($"\nTurn {turnIndex + 1}/{TurnsPerMatch} — {actor.Name} to speak");
            Console.WriteLine($"Tokens: {actor.Name}={actor.QuestionTokens} | {partner.Name}={partner.QuestionTokens}");

            // Show per-turn constraints (helpful in console)
            PrintTurnConstraints(active, lastMsg);

            Console.Write($"{actor.Name}: ");
            string msg = Console.ReadLine() ?? "";
            if (msg.Trim().Equals("end", StringComparison.OrdinalIgnoreCase)) break;

            // Apply base scoring (offline heuristics)
            var ts = ScoreTurn(msg, lastMsg, recentTokens);

            // Apply modifiers (compliance + token economy + mirror + role lock)
            bool complied = true;

            // 1) Role Lock compliance
            if (active.RoleLockEnabled)
            {
                if (!CheckRoleLock(active.RoleLock, msg, lastMsg))
                    complied = false;
            }

            // 2) Mirror Mode compliance (uses last global message; simplest)
            if (active.MirrorEnabled && !string.IsNullOrWhiteSpace(lastMsg))
            {
                if (!CheckMirror(active.Mirror, msg, lastMsg))
                    complied = false;
            }

            // 3) Question Economy token cost/constraints
            bool asked = ContainsQuestion(msg);
            if (active.QuestionEconomyEnabled && asked)
            {
                if (actor.QuestionTokens >= QuestionTokenCost)
                    actor.QuestionTokens -= QuestionTokenCost;
                else
                    complied = false; // asked without tokens
            }

            // Apply compliance bonus/penalty
            ts.CompliedAll = complied;
            if (complied)
            {
                ts.BonusIP += ComplianceBonusIP;
            }
            else
            {
                ts.PenaltyIP += CompliancePenaltyIP;
            }

            // Compute final IP for this turn
            int finalIP = Clamp(ts.IP + ts.BonusIP - ts.PenaltyIP, 0, 40);
            actor.TotalIP += finalIP;

            // Earn tokens if turn was strong (post base IP, not including bonus/penalty—tweak if you want)
            if (active.QuestionEconomyEnabled && ts.IP >= QuestionTokenEarnThresholdIP)
            {
                actor.QuestionTokens += QuestionTokenEarnCapPerTurn;
            }

            // Update memories
            UpdateRecentTokens(recentTokens, msg);
            lastMsg = msg;

            // Print feedback
            PrintTurnReport(actor, msg, ts, finalIP, active);

            // Next
            turnIndex++;
            current = 1 - current;
        }

        Console.WriteLine("\n=== MATCH OVER ===");
        Console.WriteLine($"{p1.Name}: TotalIP={p1.TotalIP} | Tokens={p1.QuestionTokens}");
        Console.WriteLine($"{p2.Name}: TotalIP={p2.TotalIP} | Tokens={p2.QuestionTokens}");
        Console.WriteLine($"Winner: {(p1.TotalIP > p2.TotalIP ? p1.Name : (p2.TotalIP > p1.TotalIP ? p2.Name : "TIE"))}");
    }

    // ---------------------------
    // Active modifiers container
    // ---------------------------
    sealed class ActiveModifiers
    {
        public bool QuestionEconomyEnabled;
        public bool MirrorEnabled;
        public bool RoleLockEnabled;

        public MirrorRule Mirror;
        public RoleLockRule RoleLock;

        public void RotateToRandomSet()
        {
            // Choose 1–2 modifiers active each rotation (keeps it readable)
            QuestionEconomyEnabled = false;
            MirrorEnabled = false;
            RoleLockEnabled = false;

            int howMany = Rng.Next(1, 3); // 1 or 2
            var pool = new List<ModifierType> { ModifierType.QuestionEconomy, ModifierType.MirrorMode, ModifierType.RoleLock };
            Shuffle(pool);

            for (int i = 0; i < howMany; i++)
            {
                var m = pool[i];
                if (m == ModifierType.QuestionEconomy) QuestionEconomyEnabled = true;
                if (m == ModifierType.MirrorMode) MirrorEnabled = true;
                if (m == ModifierType.RoleLock) RoleLockEnabled = true;
            }

            // If enabled, pick rules
            if (MirrorEnabled) Mirror = (MirrorRule)Rng.Next(Enum.GetValues(typeof(MirrorRule)).Length);
            if (RoleLockEnabled) RoleLock = (RoleLockRule)Rng.Next(Enum.GetValues(typeof(RoleLockRule)).Length);
        }
    }

    static void PrintModifiers(ActiveModifiers a)
    {
        Console.WriteLine("\n=== MODIFIERS ACTIVE ===");
        if (!a.QuestionEconomyEnabled && !a.MirrorEnabled && !a.RoleLockEnabled)
        {
            Console.WriteLine("None (core conversation only).");
            return;
        }

        if (a.QuestionEconomyEnabled) Console.WriteLine($"• Question Economy: questions cost {QuestionTokenCost} token; earn +1 token if base IP ≥ {QuestionTokenEarnThresholdIP}");
        if (a.MirrorEnabled) Console.WriteLine($"• Mirror Mode: your reply must be {MirrorRuleText(a.Mirror)} than the last message");
        if (a.RoleLockEnabled) Console.WriteLine($"• Role Lock: {RoleLockText(a.RoleLock)}");
        Console.WriteLine($"Compliance: +{ComplianceBonusIP} IP if you follow all active rules; -{CompliancePenaltyIP} IP if you violate any.");
    }

    static void PrintTurnConstraints(ActiveModifiers a, string lastMsg)
    {
        if (!a.QuestionEconomyEnabled && !a.MirrorEnabled && !a.RoleLockEnabled) return;

        Console.WriteLine("Constraints:");
        if (a.QuestionEconomyEnabled) Console.WriteLine($"  - Questions cost tokens (ask wisely).");
        if (a.MirrorEnabled && !string.IsNullOrWhiteSpace(lastMsg)) Console.WriteLine($"  - Mirror: be {MirrorRuleText(a.Mirror)} (last had {WordCount(lastMsg)} words).");
        if (a.RoleLockEnabled) Console.WriteLine($"  - Role Lock: {RoleLockText(a.RoleLock)}");
    }

    static string MirrorRuleText(MirrorRule r) =>
        r switch
        {
            MirrorRule.FewerWords => "FEWER words",
            MirrorRule.SameWords => "SAME number of words",
            _ => "MORE words",
        };

    static string RoleLockText(RoleLockRule r) =>
        r switch
        {
            RoleLockRule.OnlyQuestions => "Only QUESTIONS (must contain '?')",
            RoleLockRule.OnlyStatements => "Only STATEMENTS (no '?')",
            RoleLockRule.OnlyReflections => "Only REFLECTIONS (e.g., 'it sounds like', 'you said', 'to summarize')",
            _ => "Only EXAMPLES (e.g., 'for example', 'like when', 'for instance')",
        };

    // ---------------------------
    // Core scoring engine (offline heuristics)
    // ---------------------------
    static TurnScore ScoreTurn(string msg, string partnerLast, HashSet<string> recentTokens)
    {
        var b = ExtractBehaviors(msg, partnerLast, recentTokens);
        var cat = Categorize(msg);

        // weights by category (simple, tune later)
        (int wOC, int wIN, int wIQ, int wRF) = cat switch
        {
            TurnCategory.Question => (1, 1, 3, 0),
            TurnCategory.Reflection => (0, 3, 1, 3),
            TurnCategory.Example => (3, 1, 1, 1),
            _ => (2, 1, 1, 1),
        };

        int raw = b.OC * wOC + b.IN * wIN + b.IQ * wIQ + b.RF * wRF;

        // normalize per category
        if (!Count.ContainsKey(cat)) { Count[cat] = 0; SumRaw[cat] = 0; }
        Count[cat] += 1;
        SumRaw[cat] += raw;

        double expected = (double)SumRaw[cat] / Math.Max(1, Count[cat]);
        expected = Math.Max(1.0, expected);

        double norm = Clamp(raw / expected, 0.0, 2.5);
        int ip = (int)Clamp(Math.Round(10.0 * norm), BaseMinIP, BaseMaxIP);

        return new TurnScore { B = b, Raw = raw, Norm = norm, IP = ip };
    }

    static BehaviorVector ExtractBehaviors(string msg, string partnerLast, HashSet<string> recentTokens)
    {
        string m = (msg ?? "").ToLowerInvariant();
        string pl = (partnerLast ?? "").ToLowerInvariant();

        bool partnerAsked = pl.Contains("?");
        bool iAsked = m.Contains("?");

        int iq = iAsked ? 1 : 0;

        int rf = 0;
        if (m.StartsWith("so ") || m.Contains("it sounds like") || m.Contains("in other words") || m.Contains("to summarize") ||
            m.Contains("you said") || m.Contains("you mentioned"))
            rf = 1;

        bool cue = (m.Contains("you said") || m.Contains("you mentioned") || m.Contains("that makes sense") || m.Contains("i hear you") || m.Contains("good point"));
        bool answered = DetectAnswer(m);

        int inn = 0;
        int overlap = OverlapScore(m, pl);
        if (cue) inn = 1;
        else if (partnerAsked && answered) inn = 1;
        else if (overlap >= 2 && answered) inn = 1;

        int oc = 0;
        if (m.Length >= 60) oc++;
        if (m.Contains("because") || m.Contains("for example") || m.Contains("for instance") || m.Contains("like when") || m.Contains("in my experience")) oc++;
        if (NovelTokenCount(m, recentTokens) >= 4) oc++;

        if (partnerAsked && answered) oc++;
        if (partnerAsked && !answered && iAsked) oc--;

        return new BehaviorVector
        {
            OC = Clamp02(oc),
            IN = Clamp02(inn),
            IQ = Clamp02(iq),
            RF = Clamp02(rf),
        };
    }

    // ---------------------------
    // Modifiers
    // ---------------------------
    static bool CheckMirror(MirrorRule rule, string msg, string lastMsg)
    {
        int w1 = WordCount(msg);
        int w0 = WordCount(lastMsg);
        return rule switch
        {
            MirrorRule.FewerWords => w1 < w0,
            MirrorRule.SameWords => w1 == w0,
            _ => w1 > w0,
        };
    }

    static bool CheckRoleLock(RoleLockRule rule, string msg, string partnerLast)
    {
        string m = (msg ?? "").ToLowerInvariant();
        return rule switch
        {
            RoleLockRule.OnlyQuestions => m.Contains("?"),
            RoleLockRule.OnlyStatements => !m.Contains("?"),
            RoleLockRule.OnlyReflections => IsReflection(m),
            RoleLockRule.OnlyExamples => IsExample(m),
            _ => true
        };
    }

    static bool IsReflection(string m)
    {
        return m.Contains("it sounds like") || m.Contains("in other words") || m.Contains("to summarize") ||
               m.Contains("you said") || m.Contains("you mentioned") || m.StartsWith("so ");
    }

    static bool IsExample(string m)
    {
        return m.Contains("for example") || m.Contains("for instance") || m.Contains("like when") || m.Contains("e.g.") || m.Contains("such as");
    }

    static bool ContainsQuestion(string msg) => (msg ?? "").Contains("?");

    // ---------------------------
    // Reporting
    // ---------------------------
    static void PrintTurnReport(Player actor, string msg, TurnScore ts, int finalIP, ActiveModifiers active)
    {
        Console.WriteLine($"  => BaseScore: Beh[OC={ts.B.OC},IN={ts.B.IN},IQ={ts.B.IQ},RF={ts.B.RF}] Raw={ts.Raw} Norm={ts.Norm:0.00} IP={ts.IP}");
        if (active.QuestionEconomyEnabled || active.MirrorEnabled || active.RoleLockEnabled)
        {
            Console.WriteLine($"  => Modifiers: {(ts.CompliedAll ? "COMPLIED ✅" : "VIOLATION ❌")}  Bonus={ts.BonusIP}  Penalty={ts.PenaltyIP}  FinalTurnIP={finalIP}");
        }
        else
        {
            Console.WriteLine($"  => FinalTurnIP={finalIP}");
        }
    }

    // ---------------------------
    // Categorization + token helpers
    // ---------------------------
    static TurnCategory Categorize(string msg)
    {
        string m = (msg ?? "").ToLowerInvariant();
        if (m.Contains("?")) return TurnCategory.Question;
        if (IsReflection(m)) return TurnCategory.Reflection;
        if (IsExample(m)) return TurnCategory.Example;
        return TurnCategory.Statement;
    }

    static int WordCount(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    static bool DetectAnswer(string m)
    {
        if (m.Contains("because")) return true;
        if (m.Contains("for me")) return true;
        if (m.Contains("i think")) return true;
        if (m.Contains("i feel")) return true;
        if (m.Contains("my favorite")) return true;
        if (m.Contains("i prefer")) return true;
        if (m.Contains("i like")) return true;
        if (m.Contains("i have")) return true;
        if (m.Contains("i'm ") || m.Contains("im ")) return true;

        foreach (char ch in m)
            if (ch >= '0' && ch <= '9') return true;

        return false;
    }

    static int OverlapScore(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;

        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int count = 0;
        for (int i = 0; i < ta.Length; i++)
        {
            string x = CleanToken(ta[i]);
            if (x.Length < 4) continue;

            for (int j = 0; j < tb.Length; j++)
            {
                string y = CleanToken(tb[j]);
                if (x == y) { count++; break; }
            }
        }
        return count;
    }

    static int NovelTokenCount(string msgLower, HashSet<string> recent)
    {
        int novel = 0;
        foreach (var raw in msgLower.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string tok = CleanToken(raw);
            if (tok.Length < 4) continue;
            if (!recent.Contains(tok)) novel++;
        }
        return novel;
    }

    static void UpdateRecentTokens(HashSet<string> recent, string msg)
    {
        string m = (msg ?? "").ToLowerInvariant();
        if (recent.Count > 220) recent.Clear();

        foreach (var raw in m.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string tok = CleanToken(raw);
            if (tok.Length >= 4) recent.Add(tok);
        }
    }

    static string CleanToken(string s)
    {
        if (s == null) return "";
        return s.Trim().Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>');
    }

    // ---------------------------
    // Utility
    // ---------------------------
    static string ReadNonEmpty(string prompt, string fallback)
    {
        Console.Write($"{prompt} [{fallback}]: ");
        string s = (Console.ReadLine() ?? "").Trim();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    static int ReadInt(string prompt, int min, int max, int fallback)
    {
        Console.Write($"{prompt} [{fallback}]: ");
        string s = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return fallback;
        if (int.TryParse(s, out int v) && v >= min && v <= max) return v;
        return fallback;
    }

    static int Clamp02(int v) => v < 0 ? 0 : (v > 2 ? 2 : v);
    static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);
    static int Clamp(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}