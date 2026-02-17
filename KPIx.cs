// Program.cs — Chatter’s Guild: Challenge Coach (KPI Quests + Polygon + Discipline XP)
// Single-file, offline, no ML/no internet. Designed to feel like a "tool/game" immediately.
//
// What’s new:
// - KPI backbone (Challenge Mode only):
//    1) Answer Rate (responsiveness)
//    2) Follow-up Quality (builds on partner’s last message)
//    3) Topic Coherence (threading across turns)
// - Live “quests” prompts each turn
// - End-of-session coaching report (actionable)
// - Polygon scoring (I/L/C) kept
// - Discipline XP awarded in Challenge Mode using:
//     XP = BaseXP(KPI score) * similarity(profileVector, archetypeTarget)
//
// Commands:
// - Type "end" on your turn to finish.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class Program
{
    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("=== CHATTER’S GUILD — CHALLENGE COACH ===");
        Console.WriteLine("KPIs: Answer Rate • Follow-up Quality • Topic Coherence");
        Console.WriteLine("Type 'end' to finish.\n");

        var p1 = new Player(ReadNonEmpty("Player 1 name", "U1"));
        var p2 = new Player(ReadNonEmpty("Player 2 name", "U2"));

        Console.WriteLine("\nShow debug? (y/n)");
        bool debug = (Console.ReadLine() ?? "").Trim().ToLowerInvariant().StartsWith("y");

        // Challenge Mode only: XP enabled
        var engine = new CoachEngine(p1, p2, debug);

        Console.WriteLine("\n--- START ---");
        Console.WriteLine("Tip: Answer → Reflect → Follow-up. Example: “I think X because Y. What about you?”\n");

        int turn = 0;
        while (true)
        {
            var actor = engine.CurrentActor;
            Console.WriteLine($"\nTurn {turn + 1} — {actor.Name}");
            engine.PrintLiveQuestsFor(actor);

            Console.Write($"{actor.Name}: ");
            string msg = Console.ReadLine() ?? "";
            if (msg.Trim().Equals("end", StringComparison.OrdinalIgnoreCase)) break;

            engine.ProcessTurn(msg);
            turn++;
        }

        Console.WriteLine("\n=== SESSION REPORT ===");
        engine.PrintSessionReport();

        Console.WriteLine("\nDone.");
    }

    static string ReadNonEmpty(string prompt, string fallback)
    {
        Console.Write($"{prompt} [{fallback}]: ");
        string s = (Console.ReadLine() ?? "").Trim();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }
}

// ---------------------------
// COACH ENGINE (KPIs + scoring)
// ---------------------------
public sealed class CoachEngine
{
    readonly Player[] order;
    int currentIndex = 0;

    readonly bool debug;

    // Conversation memory
    readonly HashSet<string> recentTokens = new HashSet<string>();
    string lastGlobal = "";
    string lastP1 = "";
    string lastP2 = "";

    // Rolling topic window for coherence
    readonly Queue<HashSet<string>> lastTopicWindows = new Queue<HashSet<string>>(); // last 4 turns tokens

    public CoachEngine(Player p1, Player p2, bool debug)
    {
        order = new[] { p1, p2 };
        this.debug = debug;
    }

    public Player CurrentActor => order[currentIndex];
    public Player CurrentPartner => order[1 - currentIndex];

    public void PrintLiveQuestsFor(Player actor)
    {
        // Quests are based on partner’s last message and actor’s recent KPI misses
        string partnerLast = actor == order[0] ? lastP2 : lastP1;
        bool partnerAsked = partnerLast.Contains("?");

        var quests = new List<string>();

        // Quest: answer before asking
        if (partnerAsked)
            quests.Add("Quest: Answer their question *before* asking a new one.");

        // Quest: follow-up using their keywords
        var partnerKeys = TextUtil.KeyTokens(partnerLast);
        if (partnerKeys.Count > 0)
            quests.Add($"Quest: Use 1 of their keywords: {string.Join(", ", partnerKeys.Take(4))}");

        // Quest: maintain thread
        if (lastTopicWindows.Count > 0)
            quests.Add("Quest: Stay on the current thread (reuse a concept from the last 2 turns).");

        // Keep it light: show up to 2
        foreach (var q in quests.Take(2))
            Console.WriteLine("  " + q);
    }

    public void ProcessTurn(string msg)
    {
        var actor = CurrentActor;
        var partner = CurrentPartner;

        string partnerLast = actor == order[0] ? lastP2 : lastP1;

        // --- KPI evaluation ---
        var kpi = KpiScorer.ScoreTurn(msg, partnerLast, lastTopicWindows);

        // --- Polygon axes (I/L/C) via S/E/R heuristics ---
        AxesResult ar = AxesScorer.ScoreAxes(msg, partnerLast, lastGlobal, recentTokens);

        // --- Combine into “Turn Points” ---
        // Base from axes, but *gated* and shaped by KPIs so the game feels real.
        int ipBase = AxesScorer.ComputeIP(ar.S, ar.E, ar.R, ar.C);
        double kpiGate = 0.55 + 0.45 * kpi.TurnKpi01;  // 0.55..1.00
        int ip = (int)Math.Round(ipBase * kpiGate);

        actor.AddTurn(ar, kpi, ip);

        Console.WriteLine($"  => KPIs: Ans={(kpi.Answered ? "✓" : "—")}  FUp={kpi.FollowUpScore:0.00}  Coh={kpi.CoherenceScore:0.00}");
        Console.WriteLine($"  => Axes: S={ar.S} E={ar.E} R={ar.R} C={ar.C:0.00} | IP=+{ip}");

        if (debug)
        {
            Console.WriteLine($"     [dbg] overlap={ar.OverlapCount} novel={ar.NovelTokens} conn={ar.Connectors} refl={ar.ReflectionMarkers} ex={ar.ExampleMarkers}");
            Console.WriteLine($"     [dbg] kpi_turn01={kpi.TurnKpi01:0.00} partnerAsked={partnerLast.Contains("?")}");
        }

        // Update token memory
        AxesScorer.UpdateRecentTokens(recentTokens, msg);
        lastGlobal = msg;

        // Update per-player last
        if (actor == order[0]) lastP1 = msg; else lastP2 = msg;

        // Update coherence window
        TextUtil.PushTopicWindow(lastTopicWindows, TextUtil.KeyTokenSet(msg), maxWindows: 4);

        // Next turn
        currentIndex = 1 - currentIndex;
    }

    public void PrintSessionReport()
    {
        // Print player KPI + polygon + discipline XP
        foreach (var p in order)
        {
            Console.WriteLine($"\n{p.Name}");
            Console.WriteLine($"  Turns: {p.Turns}");
            Console.WriteLine($"  Total IP: {p.TotalIP}");
            Console.WriteLine($"  Avg IP/turn: {(p.Turns == 0 ? 0 : (double)p.TotalIP / p.Turns):0.00}");

            // KPI report
            var rep = p.KpiReport();
            Console.WriteLine($"  KPI Answer Rate: {rep.AnswerRate:0.0}%");
            Console.WriteLine($"  KPI Follow-up:   {rep.FollowUpAvg:0.00}");
            Console.WriteLine($"  KPI Coherence:   {rep.CoherenceAvg:0.00}");
            Console.WriteLine($"  KPI Score:       {rep.KpiScore100:0.0}/100");

            // Coaching hints (actionable)
            foreach (var tip in rep.CoachTips)
                Console.WriteLine("  Tip: " + tip);

            // Polygon (I/L/C)
            var v = p.SessionVectorILC();
            Console.WriteLine("  Polygon (I/L/C):");
            Console.WriteLine($"    Initiator  {Bar(v.I)} {v.I:0.00}");
            Console.WriteLine($"    Listener   {Bar(v.L)} {v.L:0.00}");
            Console.WriteLine($"    Challenger {Bar(v.C)} {v.C:0.00}");

            // Discipline XP (Challenge Mode)
            Console.WriteLine("  Discipline XP (Challenge Mode):");
            p.AwardDisciplineXP(rep.KpiScore100);
        }

        // Winner by Total IP
        var w = order[0].TotalIP > order[1].TotalIP ? order[0].Name : (order[1].TotalIP > order[0].TotalIP ? order[1].Name : "TIE");
        Console.WriteLine($"\nWinner (Total IP): {w}");
    }

    static string Bar(double x)
    {
        int width = 16;
        int fill = (int)Math.Round(width * Clamp(x, 0.0, 1.0));
        return "[" + new string('█', fill) + new string('·', width - fill) + "]";
    }
    static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);
}

// ---------------------------
// KPI SCORING (core “coach/game”)
// ---------------------------
public struct TurnKpi
{
    public bool PartnerAsked;
    public bool Answered;
    public bool AskedNewQuestion;
    public double FollowUpScore;   // 0..1
    public double CoherenceScore;  // 0..1
    public double TurnKpi01;       // 0..1 aggregate
}

public static class KpiScorer
{
    // “Answered” cues: simple and cheap (improve later)
    static readonly string[] AnswerCues = {
        "i think","i feel","because","for me","in my case","my favorite","i prefer","i like","i have","i'm ","im ",
        "yes","no","maybe","probably","honestly"
    };

    // “Follow-up” cues / reflection
    static readonly string[] FollowCues = {
        "you said","you mentioned","it sounds like","i hear you","that makes sense","good point","so you're saying","so you are saying"
    };

    public static TurnKpi ScoreTurn(string msg, string partnerLast, Queue<HashSet<string>> topicWindows)
    {
        string m = (msg ?? "");
        string ml = m.ToLowerInvariant();
        string pl = (partnerLast ?? "");
        string pll = pl.ToLowerInvariant();

        bool partnerAsked = pl.Contains("?");
        bool askedNewQ = m.Contains("?");

        // Answer detection: if partner asked, we want some answer content before another question
        bool answered = false;
        if (partnerAsked)
        {
            answered = ContainsAny(ml, AnswerCues) || TextUtil.ContainsDigit(ml) || TextUtil.WordCount(m) >= 8;
            // If they only asked a question and didn’t provide any answer-ish content, treat as not answered
            if (askedNewQ && !ContainsAny(ml, AnswerCues) && !TextUtil.ContainsDigit(ml) && TextUtil.WordCount(m) < 12)
                answered = false;
        }

        // Follow-up quality: overlap with partner key tokens OR reflection cue
        var partnerKeys = TextUtil.KeyTokenSet(pll);
        var myKeys = TextUtil.KeyTokenSet(ml);

        int overlap = TextUtil.OverlapCount(myKeys, partnerKeys);
        double overlapNorm = overlap >= 4 ? 1.0 : (overlap >= 2 ? 0.7 : (overlap >= 1 ? 0.4 : 0.0));
        double reflectBonus = ContainsAny(ml, FollowCues) ? 0.35 : 0.0;
        double followUp = Clamp01(overlapNorm + reflectBonus); // 0..1

        // Topic coherence: overlap with recent topic windows (last 2 turns favored)
        double coherence = 0.0;
        if (topicWindows.Count > 0)
        {
            var arr = topicWindows.ToArray();
            // last 2 windows weighted more
            double best = 0.0;
            for (int i = 0; i < arr.Length; i++)
            {
                int wOverlap = TextUtil.OverlapCount(myKeys, arr[arr.Length - 1 - i]);
                double local = wOverlap >= 4 ? 1.0 : (wOverlap >= 2 ? 0.7 : (wOverlap >= 1 ? 0.35 : 0.0));
                // weight: most recent gets 1.0, then 0.7, then 0.5...
                double weight = i == 0 ? 1.0 : (i == 1 ? 0.7 : 0.5);
                best = Math.Max(best, local * weight);
            }
            coherence = Clamp01(best);
        }

        // Aggregate turn KPI:
        // - Answer matters only when partner asked
        // - Follow-up always matters
        // - Coherence always matters
        double ansPart = partnerAsked ? (answered ? 1.0 : 0.0) : 1.0; // no penalty if no question asked
        double turnKpi = 0.40 * ansPart + 0.35 * followUp + 0.25 * coherence;

        return new TurnKpi
        {
            PartnerAsked = partnerAsked,
            Answered = answered,
            AskedNewQuestion = askedNewQ,
            FollowUpScore = followUp,
            CoherenceScore = coherence,
            TurnKpi01 = Clamp01(turnKpi)
        };
    }

    static bool ContainsAny(string textLower, string[] phrases)
    {
        foreach (var p in phrases)
            if (textLower.Contains(p)) return true;
        return false;
    }

    static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}

// ---------------------------
// AXES SCORING (Polygon backbone)
// ---------------------------
public struct AxesResult
{
    public int S;      // Structure 0..10 (Initiator proxy)
    public int E;      // Elevation 0..10 (Challenger proxy)
    public int R;      // Reciprocity 0..10 (Listener proxy)
    public double C;   // Clarity 0.75..1.10

    // debug
    public int Connectors;
    public int ReflectionMarkers;
    public int ExampleMarkers;
    public int OverlapCount;
    public int NovelTokens;
}

public static class AxesScorer
{
    // Keep small for now; expand later (your blob lexicon approach fits here too)
    static readonly string[] Connectors = { "because","therefore","however","although","on the other hand","whereas","despite","nevertheless" };
    static readonly string[] Reflection = { "it sounds like","you said","you mentioned","i hear you","that makes sense","good point","in other words","to summarize" };
    static readonly string[] Examples   = { "for example","for instance","such as","like when" };

    static readonly HashSet<string> Stop = new HashSet<string>(new[]{
        "the","a","an","and","or","but","if","to","of","in","on","for","with","at","by","from","is","are","was","were",
        "it","this","that","these","those","i","you","we","they","he","she","me","my","your","our","their","them","us",
        "as","so","not","do","did","does","can","could","would","should","will","just","then","than"
    }, StringComparer.OrdinalIgnoreCase);

    public static AxesResult ScoreAxes(string message, string partnerLast, string lastMessage, HashSet<string> recentTokens)
    {
        string msg = message ?? "";
        string m = msg.ToLowerInvariant();
        string pl = (partnerLast ?? "").ToLowerInvariant();
        string lm = (lastMessage ?? "").ToLowerInvariant();

        int wc = TextUtil.WordCount(msg);
        bool partnerAsked = pl.Contains("?");
        bool iAsked = msg.Contains("?");

        int conn = CountAny(m, Connectors, cap: 2);
        int refl = CountAny(m, Reflection, cap: 2);
        int ex   = CountAny(m, Examples, cap: 1);

        int overlap = TextUtil.OverlapCount(TextUtil.KeyTokenSet(m), TextUtil.KeyTokenSet(pl));
        int novel = NovelTokenCount(m, recentTokens);

        bool answered = DetectAnswer(m);

        // Structure S
        int S2 = wc >= 45 ? 2 : (wc >= 18 ? 1 : 0);
        int S4 = novel >= 7 ? 2 : (novel >= 3 ? 1 : 0);
        int S5 = (partnerAsked && answered) ? 2 : 0;
        int Sraw = 2 * conn + 2 * S5 + S2 + ex + S4;
        int S = ClampInt(Sraw, 0, 10);

        // Elevation E (proxy): questions + connectors + “what if” style
        int elev = 0;
        if (iAsked) elev += 2;
        if (m.Contains("what if") || m.Contains("imagine")) elev += 2;
        elev += conn;
        elev += wc >= 25 ? 1 : 0;
        int E = ClampInt(elev, 0, 10);

        // Reciprocity R: reflections + overlap + answer-then-ask
        int Rraw = 2 * refl + (overlap >= 4 ? 3 : (overlap >= 2 ? 2 : (overlap >= 1 ? 1 : 0))) + ((partnerAsked && answered && iAsked) ? 2 : 0);
        int R = ClampInt(Rraw, 0, 10);

        // Clarity C
        double C = 1.0;
        if (wc <= 2) C -= 0.20;
        else if (wc <= 5) C -= 0.10;
        if (!string.IsNullOrWhiteSpace(lm) && m.Trim() == lm.Trim()) C -= 0.20;
        if (TextUtil.RepeatedTokenRatio(m, Stop) > 0.40 && wc >= 10) C -= 0.10;
        if (TextUtil.HasPunctuation(msg)) C += (wc >= 10 ? 0.05 : 0.0);
        if (conn > 0) C += 0.05;
        C = ClampDouble(C, 0.75, 1.10);

        return new AxesResult
        {
            S = S, E = E, R = R, C = C,
            Connectors = conn,
            ReflectionMarkers = refl,
            ExampleMarkers = ex,
            OverlapCount = overlap,
            NovelTokens = novel
        };
    }

    public static int ComputeIP(int S, int E, int R, double C)
    {
        double baseScore = 0.45 * S + 0.35 * E + 0.20 * R; // 0..10
        double scaled = baseScore * 3.0;                   // 0..30
        int ip = (int)Math.Round(scaled * C);
        return ClampInt(ip, 0, 30);
    }

    public static void UpdateRecentTokens(HashSet<string> recent, string msg)
    {
        string m = (msg ?? "").ToLowerInvariant();
        if (recent.Count > 220) recent.Clear();
        foreach (var t in TextUtil.Tokenize(m))
            if (t.Length >= 4 && !Stop.Contains(t))
                recent.Add(t);
    }

    static int CountAny(string textLower, string[] phrases, int cap)
    {
        int c = 0;
        foreach (var p in phrases)
            if (textLower.Contains(p) && ++c >= cap) return cap;
        return c;
    }

    static bool DetectAnswer(string m)
    {
        if (m.Contains("because")) return true;
        if (m.Contains("for me")) return true;
        if (m.Contains("i think")) return true;
        if (m.Contains("i feel")) return true;
        if (m.Contains("my favorite")) return true;
        if (m.Contains("i like")) return true;
        if (m.Contains("i have")) return true;
        if (m.Contains("i'm ") || m.Contains("im ")) return true;
        if (TextUtil.ContainsDigit(m)) return true;
        return false;
    }

    static int NovelTokenCount(string msgLower, HashSet<string> recent)
    {
        int novel = 0;
        foreach (var t in TextUtil.Tokenize(msgLower))
        {
            if (t.Length < 4 || Stop.Contains(t)) continue;
            if (!recent.Contains(t)) novel++;
        }
        return novel;
    }

    static int ClampInt(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);
    static double ClampDouble(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);
}

// ---------------------------
// PLAYER + DISCIPLINES (XP)
// ---------------------------
public sealed class Player
{
    public string Name;
    public int Turns { get; private set; }
    public int TotalIP { get; private set; }

    int sumS, sumE, sumR;
    int questionsFaced;     // partner asked
    int questionsAnswered;  // answered
    double sumFollow;
    double sumCoh;

    public Dictionary<string, Discipline> Disciplines { get; } = Discipline.DefaultSet();

    public Player(string name) { Name = name; }

    public void AddTurn(AxesResult ar, TurnKpi kpi, int ip)
    {
        Turns++;
        TotalIP += ip;

        sumS += ar.S; sumE += ar.E; sumR += ar.R;

        if (kpi.PartnerAsked)
        {
            questionsFaced++;
            if (kpi.Answered) questionsAnswered++;
        }

        sumFollow += kpi.FollowUpScore;
        sumCoh += kpi.CoherenceScore;
    }

    public (double AnswerRate, double FollowUpAvg, double CoherenceAvg, double KpiScore100, List<string> CoachTips) KpiReport()
    {
        double ansRate = questionsFaced == 0 ? 100.0 : (100.0 * questionsAnswered / Math.Max(1, questionsFaced));
        double fup = Turns == 0 ? 0 : sumFollow / Turns;
        double coh = Turns == 0 ? 0 : sumCoh / Turns;

        // KPI score 0..100
        // Answer rate matters, but if no questions faced, it won’t inflate unfairly (it becomes neutral at 100).
        double score = 0.40 * (ansRate / 100.0) + 0.35 * fup + 0.25 * coh;
        double score100 = 100.0 * Clamp01(score);

        var tips = new List<string>();
        if (ansRate < 75 && questionsFaced >= 2) tips.Add("Answer more directly before pivoting or asking a new question.");
        if (fup < 0.55) tips.Add("Ask follow-ups using their keywords (reuse 1–2 words they used).");
        if (coh < 0.55) tips.Add("Stay on a thread for 2–3 turns; give one example before switching topics.");
        if (tips.Count == 0) tips.Add("Great fundamentals — try adding one reflection + one example each round.");

        return (ansRate, fup, coh, score100, tips);
    }

    public (double I, double L, double C) SessionVectorILC()
    {
        if (Turns == 0) return (0, 0, 0);

        // Raw averages in 0..1
        double I = (double)sumS / (Turns * 10.0); // Structure -> Initiator
        double L = (double)sumR / (Turns * 10.0); // Reciprocity -> Listener
        double C = (double)sumE / (Turns * 10.0); // Elevation  -> Challenger

        double total = I + L + C;
        if (total <= 1e-9) total = 1.0;

        return (I / total, L / total, C / total);
    }

    public void AwardDisciplineXP(double kpiScore100)
    {
        int baseXp = BaseXPFromKpi(kpiScore100);

        if (baseXp == 0)
        {
            Console.WriteLine("    (No XP: raise KPI score to earn progression in Challenge Mode.)");
            return;
        }

        var v = SessionVectorILC();
        var sessionVec = new Vec3(v.I, v.L, v.C);

        bool any = false;
        foreach (var d in Disciplines.Values)
        {
            double sim = Vec3.Cosine(sessionVec, d.Target);
            if (sim >= 0.82)
            {
                int gain = (int)Math.Round(baseXp * sim);
                d.AddXP(gain);
                any = true;
                Console.WriteLine($"    {d.Name,-12} +{gain} XP  (sim {sim:0.00})  Lv {d.Level}  XP {d.XP}/{d.NextThreshold()}");
            }
        }

        if (!any)
            Console.WriteLine("    (No discipline matched strongly — try leaning into a style for a few turns.)");
    }

    static int BaseXPFromKpi(double kpiScore100)
    {
        if (kpiScore100 < 55) return 0;
        if (kpiScore100 < 70) return 25;
        if (kpiScore100 < 85) return 45;
        return 65;
    }

    static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}

// ---------------------------
// DISCIPLINES (XP)
// ---------------------------
public sealed class Discipline
{
    public string Name;
    public Vec3 Target;
    public int XP;
    public int Level;

    public Discipline(string name, Vec3 target)
    {
        Name = name;
        Target = target;
        Level = 1;
        XP = 0;
    }

    public int NextThreshold() => 100 + (Level - 1) * 40;

    public void AddXP(int amount)
    {
        XP += Math.Max(0, amount);
        while (XP >= NextThreshold())
        {
            XP -= NextThreshold();
            Level++;
        }
    }

    public static Dictionary<string, Discipline> DefaultSet()
    {
        return new Dictionary<string, Discipline>
        {
            { "Medium",      new Discipline("Medium",      new Vec3(0.45,0.45,0.10)) },
            { "Sniper",      new Discipline("Sniper",      new Vec3(0.15,0.45,0.40)) },
            { "Provocateur", new Discipline("Provocateur", new Vec3(0.45,0.10,0.45)) },
            { "Harmonizer",  new Discipline("Harmonizer",  new Vec3(0.30,0.50,0.20)) },
            { "Clarifier",   new Discipline("Clarifier",   new Vec3(0.20,0.40,0.40)) },
            { "Architect",   new Discipline("Architect",   new Vec3(0.33,0.33,0.33)) }
        };
    }
}

public struct Vec3
{
    public double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static double Mag(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    public static double Cosine(Vec3 a, Vec3 b)
    {
        double denom = Mag(a) * Mag(b);
        if (denom <= 1e-12) return 0;
        return Dot(a, b) / denom;
    }
}

// ---------------------------
// TEXT UTIL
// ---------------------------
public static class TextUtil
{
    static readonly char[] Splitters = new[] { ' ', '\t', '\r', '\n' };

    public static int WordCount(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return s.Split(Splitters, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static bool HasPunctuation(string s)
        => (s ?? "").IndexOfAny(new[] { '.', ',', ';', ':', '-', '!' }) >= 0;

    public static bool ContainsDigit(string s)
    {
        foreach (char c in s) if (c >= '0' && c <= '9') return true;
        return false;
    }

    public static List<string> Tokenize(string lower)
    {
        var parts = (lower ?? "").ToLowerInvariant()
            .Split(Splitters, StringSplitOptions.RemoveEmptyEntries);

        var toks = new List<string>(parts.Length);
        foreach (var raw in parts)
        {
            string t = Clean(raw);
            if (t.Length > 0) toks.Add(t);
        }
        return toks;
    }

    static string Clean(string s)
    {
        if (s == null) return "";
        return s.Trim().Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>', '-', '_');
    }

    public static HashSet<string> KeyTokenSet(string text)
    {
        var set = new HashSet<string>();
        foreach (var t in Tokenize((text ?? "").ToLowerInvariant()))
            if (t.Length >= 4) set.Add(t);
        return set;
    }

    public static List<string> KeyTokens(string text)
        => KeyTokenSet(text).Take(8).ToList();

    public static int OverlapCount(HashSet<string> a, HashSet<string> b)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0) return 0;
        int c = 0;
        foreach (var x in a) if (b.Contains(x)) c++;
        return c;
    }

    public static void PushTopicWindow(Queue<HashSet<string>> q, HashSet<string> tokens, int maxWindows)
    {
        q.Enqueue(tokens ?? new HashSet<string>());
        while (q.Count > maxWindows) q.Dequeue();
    }

    public static double RepeatedTokenRatio(string msgLower, HashSet<string> stop)
    {
        var toks = Tokenize(msgLower).Where(t => t.Length >= 3 && (stop == null || !stop.Contains(t))).ToList();
        if (toks.Count < 6) return 0.0;

        var freq = new Dictionary<string, int>();
        foreach (var t in toks)
        {
            if (!freq.ContainsKey(t)) freq[t] = 0;
            freq[t]++;
        }
        int max = freq.Values.Max();
        return (double)max / toks.Count;
    }
}