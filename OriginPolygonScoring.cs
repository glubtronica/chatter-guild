// Program.cs
// Chatter's Guild - Offline Conversation Lab (2 players, strict turn-taking)
// Uses S/E/R/C axes scoring (Structure, Elevation, Reciprocity, Clarity) + IP per turn.
// No external libraries. No server. No internet.
//
// How to use:
// - Run
// - Enter player names (or accept defaults)
// - Take turns typing messages
// - Type "end" on your turn to finish
// - Get a session report + per-player archetype lean (3-role mapping) + averages

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public struct AxesResult
{
    public int S;      // Structure 0..10
    public int E;      // Elevation 0..10
    public int R;      // Reciprocity 0..10
    public double C;   // Clarity multiplier 0.75..1.10

    // Optional debug counters (handy while tuning)
    public int Connectors;
    public int Introspection;
    public int MeaningMarkers;
    public int Vulnerability;
    public int Perspective;
    public int Synthesis;
    public int ReflectionMarkers;
    public int ExampleMarkers;
    public int OverlapCount;
    public int NovelTokens;
}

public static class AxesScorer
{
    // ---- Phrase dictionaries (small; tune later) ----
    static readonly string[] Connectors = {
        "because","therefore","thus","so","however","although","though","but","yet",
        "on the other hand","whereas","if ","unless","when ","while "
    };

    static readonly string[] Introspection = {
        "i feel","i fear","i worry","i'm excited","im excited","i'm grateful","im grateful",
        "i struggle","i'm scared","im scared","i'm anxious","im anxious","i'm glad","im glad",
        "i believe","i care","i value"
    };

    static readonly string[] Meaning = {
        "it matters","matters","meaningful","important","purpose","value","values","identity","the point is"
    };

    static readonly string[] Vulnerability = {
        "i'm not sure","im not sure","i don't know","i do not know","i wonder",
        "maybe","i'm afraid","im afraid","i feel stuck","i struggle"
    };

    static readonly string[] Perspective = {
        "what if","imagine","from your perspective","i can see how","i see why","on the other hand"
    };

    static readonly string[] Synthesis = {
        "that connects to","this connects to","bigger picture","pattern","what i'm realizing","what im realizing",
        "i learned","it taught me","i'm realizing","im realizing"
    };

    static readonly string[] Reflection = {
        "it sounds like","in other words","to summarize","so you're saying","so you are saying",
        "you said","you mentioned","i hear you","that makes sense","good point"
    };

    static readonly string[] Examples = {
        "for example","for instance","like when","such as","e.g.","eg "
    };

    // Tiny stopword list (for novelty/overlap signal)
    static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","if","to","of","in","on","for","with","at","by","from",
        "is","are","was","were","be","been","being","it","this","that","these","those",
        "i","you","we","they","he","she","me","my","your","our","their","them","us",
        "as","so","not","do","did","does","can","could","would","should","will","just"
    };

    public static AxesResult ScoreAxes(string message, string partnerLast, string lastMessage, HashSet<string> recentTokens)
    {
        string msg = (message ?? "");
        string m = msg.ToLowerInvariant();
        string pl = (partnerLast ?? "").ToLowerInvariant();
        string lm = (lastMessage ?? "").ToLowerInvariant();

        int wc = WordCount(msg);
        bool partnerAsked = pl.Contains("?");
        bool iAsked = msg.Contains("?");

        int connectors = CountAny(m, Connectors, cap: 2);
        int introspection = CountAny(m, Introspection, cap: 2);
        int meaning = CountAny(m, Meaning, cap: 2);
        int vulnerability = CountAny(m, Vulnerability, cap: 2);
        int perspective = CountAny(m, Perspective, cap: 2);
        int synthesis = CountAny(m, Synthesis, cap: 2);
        int reflection = CountAny(m, Reflection, cap: 2);
        int examples = CountAny(m, Examples, cap: 2);

        int overlap = OverlapScore(m, pl);
        int novel = NovelTokenCount(m, recentTokens);

        bool answered = DetectAnswer(m);

        // -------- Structure S (0..10) --------
        int S1 = connectors;                                // 0..2
        int S2 = (wc >= 45) ? 2 : (wc >= 18 ? 1 : 0);       // 0..2
        int S3 = examples > 0 ? 1 : 0;                      // 0..1
        int S4 = (novel >= 7) ? 2 : (novel >= 3 ? 1 : 0);   // 0..2
        int S5 = (partnerAsked && answered) ? 2 : 0;        // 0..2

        int Sraw = 2 * S1 + 2 * S5 + S2 + S3 + S4;          // ~0..11
        int S = ClampInt(Sraw, 0, 10);

        // -------- Elevation E (0..10) --------
        int E1 = introspection;   // 0..2
        int E2 = meaning;         // 0..2
        int E3 = vulnerability;   // 0..2
        int E4 = perspective;     // 0..2
        int E5 = synthesis;       // 0..2

        int Eraw = 2 * E1 + 2 * E2 + 2 * E3 + 2 * E4 + 2 * E5; // 0..20
        int E = ClampInt((int)Math.Round(Eraw / 2.0), 0, 10);

        // -------- Reciprocity R (0..10) --------
        int R1 = reflection; // 0..2

        int R2 = 0;
        if (overlap >= 4) R2 = 3;
        else if (overlap >= 2) R2 = 2;

        int R3 = (partnerAsked && answered && iAsked) ? 2 : 0;

        int R4 = 0;
        bool hasYou = ContainsWord(m, "you");
        bool hasI = ContainsWord(m, "i");
        bool hasWe = ContainsWord(m, "we");
        if (hasYou && (hasI || hasWe)) R4++;
        if (hasWe) R4++;

        int R5 = 0;
        if (partnerAsked && iAsked && !answered) R5 -= 2;
        if (partnerAsked && wc <= 3) R5 -= 2;

        int Rraw = 3 * R2 + 2 * R3 + R1 + R4 + R5;
        int R = ClampInt(Rraw, 0, 10);

        // -------- Clarity multiplier C (0.75..1.10) --------
        double C = 1.00;

        if (wc <= 2) C -= 0.20;
        else if (wc <= 5) C -= 0.10;

        bool hasPunct = msg.IndexOfAny(new[] { '.', ',', ';', ':', '—', '-', '!' }) >= 0;
        if (wc >= 70 && !hasPunct) C -= 0.10;

        if (!string.IsNullOrWhiteSpace(lm) && m.Trim() == lm.Trim()) C -= 0.20;
        if (RepeatedTokenRatio(m) > 0.40 && wc >= 10) C -= 0.10;

        if (IsAllCaps(msg) && wc > 4) C -= 0.10;
        if (msg.Contains("!!!!") || msg.Contains("????") || msg.Contains("!!!") || msg.Contains("???")) C -= 0.05;

        if (wc >= 10 && hasPunct) C += 0.05;
        if (connectors > 0) C += 0.05;

        C = ClampDouble(C, 0.75, 1.10);

        return new AxesResult
        {
            S = S, E = E, R = R, C = C,
            Connectors = connectors,
            Introspection = introspection,
            MeaningMarkers = meaning,
            Vulnerability = vulnerability,
            Perspective = perspective,
            Synthesis = synthesis,
            ReflectionMarkers = reflection,
            ExampleMarkers = examples,
            OverlapCount = overlap,
            NovelTokens = novel
        };
    }

    public static int ComputeIP(int S, int E, int R, double C)
    {
        // 0..10 blended -> 0..30 scaled; then clarity multiplier
        double baseScore = 0.45 * S + 0.35 * E + 0.20 * R; // 0..10
        double scaled = baseScore * 3.0;                   // 0..30
        int ip = (int)Math.Round(scaled * C);
        return ClampInt(ip, 0, 30);
    }

    public static void UpdateRecentTokens(HashSet<string> recent, string msg)
    {
        string m = (msg ?? "").ToLowerInvariant();
        if (recent.Count > 220) recent.Clear();

        foreach (var t in Tokenize(m))
            if (t.Length >= 4 && !Stop.Contains(t))
                recent.Add(t);
    }

    // ---------------- Helpers ----------------
    static int CountAny(string textLower, string[] phrases, int cap)
    {
        int c = 0;
        foreach (var p in phrases)
        {
            if (textLower.Contains(p))
            {
                c++;
                if (c >= cap) return cap;
            }
        }
        return c;
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

    static int WordCount(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    static bool ContainsWord(string textLower, string word)
    {
        string w = word.ToLowerInvariant();
        if (textLower == w) return true;
        if (textLower.StartsWith(w + " ")) return true;
        if (textLower.EndsWith(" " + w)) return true;
        return textLower.Contains(" " + w + " ");
    }

    static int OverlapScore(string aLower, string bLower)
    {
        if (string.IsNullOrEmpty(aLower) || string.IsNullOrEmpty(bLower)) return 0;

        var ta = Tokenize(aLower);
        var tb = Tokenize(bLower);

        int count = 0;
        for (int i = 0; i < ta.Count; i++)
        {
            string x = ta[i];
            if (x.Length < 4 || Stop.Contains(x)) continue;

            for (int j = 0; j < tb.Count; j++)
            {
                if (x == tb[j]) { count++; break; }
            }
        }
        return count;
    }

    static int NovelTokenCount(string msgLower, HashSet<string> recent)
    {
        int novel = 0;
        foreach (var t in Tokenize(msgLower))
        {
            if (t.Length < 4 || Stop.Contains(t)) continue;
            if (!recent.Contains(t)) novel++;
        }
        return novel;
    }

    static List<string> Tokenize(string lower)
    {
        var parts = lower.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var toks = new List<string>(parts.Length);
        foreach (var raw in parts)
        {
            string tok = CleanToken(raw);
            if (tok.Length > 0) toks.Add(tok);
        }
        return toks;
    }

    static string CleanToken(string s)
    {
        if (s == null) return "";
        return s.Trim().Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>', '—', '-', '_');
    }

    static double RepeatedTokenRatio(string msgLower)
    {
        var toks = Tokenize(msgLower).Where(t => t.Length >= 3 && !Stop.Contains(t)).ToList();
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

    static bool IsAllCaps(string s)
    {
        bool hasLetter = false;
        foreach (char ch in s)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
                if (!char.IsUpper(ch)) return false;
            }
        }
        return hasLetter;
    }

    static int ClampInt(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);
    static double ClampDouble(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);
}

public sealed class Player
{
    public string Name;
    public int TotalIP;
    public int Turns;

    public int SumS, SumE, SumR;
    public double SumC;

    // Simple archetype evidence mapping (3 “disciplines” for now):
    // Initiator ~ Structure, Listener ~ Reciprocity, Challenger ~ Elevation (proxy)
    public double ArcheI, ArcheL, ArcheC;

    public Player(string name) { Name = name; }

    public void AddTurn(int S, int E, int R, double C, int ip)
    {
        TotalIP += ip;
        Turns++;

        SumS += S;
        SumE += E;
        SumR += R;
        SumC += C;

        // quality-weighted evidence
        double q = Math.Max(0.0, Math.Min(1.0, ip / 18.0));
        ArcheI += (S / 10.0) * (1.0 + 0.4 * q);
        ArcheL += (R / 10.0) * (1.0 + 0.4 * q);
        ArcheC += (E / 10.0) * (1.0 + 0.4 * q);
    }
}

public static class Program
{
    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("=== Chatter’s Guild: Offline Conversation Lab (S/E/R/C) ===");
        Console.WriteLine("Two players • strict turn-taking • type 'end' to finish\n");

        var p1 = new Player(ReadNonEmpty("Player 1 name", "U1"));
        var p2 = new Player(ReadNonEmpty("Player 2 name", "U2"));

        Console.WriteLine("\nShow debug counters each turn? (y/n)");
        bool debug = (Console.ReadLine() ?? "").Trim().ToLowerInvariant().StartsWith("y");

        Console.WriteLine("\n--- START ---");
        Console.WriteLine("Tips: use 'because/however', reflect ('it sounds like...'), ask & answer.\n");

        var recentTokens = new HashSet<string>();
        string lastGlobal = "";
        string lastP1 = "";
        string lastP2 = "";

        Player[] order = { p1, p2 };
        int current = 0;
        int turnIndex = 0;

        while (true)
        {
            var actor = order[current];
            var partner = order[1 - current];

            string partnerLast = (actor == p1) ? lastP2 : lastP1;

            Console.WriteLine($"\nTurn {turnIndex + 1} — {actor.Name}");
            Console.Write($"{actor.Name}: ");
            string msg = Console.ReadLine() ?? "";

            if (msg.Trim().Equals("end", StringComparison.OrdinalIgnoreCase))
                break;

            // Score (before updating recent tokens)
            AxesResult ar = AxesScorer.ScoreAxes(msg, partnerLast, lastGlobal, recentTokens);
            int ip = AxesScorer.ComputeIP(ar.S, ar.E, ar.R, ar.C);

            // Update per-player totals
            actor.AddTurn(ar.S, ar.E, ar.R, ar.C, ip);

            // Print summary for the turn
            Console.WriteLine($"  => S={ar.S} E={ar.E} R={ar.R} C={ar.C:0.00} | IP=+{ip}");

            if (debug)
            {
                Console.WriteLine($"     [dbg] conn={ar.Connectors} refl={ar.ReflectionMarkers} ex={ar.ExampleMarkers} " +
                                  $"novel={ar.NovelTokens} overlap={ar.OverlapCount} " +
                                  $"intro={ar.Introspection} meaning={ar.MeaningMarkers} vuln={ar.Vulnerability} " +
                                  $"persp={ar.Perspective} synth={ar.Synthesis}");
            }

            // Update memories AFTER scoring
            AxesScorer.UpdateRecentTokens(recentTokens, msg);
            lastGlobal = msg;

            if (actor == p1) lastP1 = msg;
            else lastP2 = msg;

            // next
            turnIndex++;
            current = 1 - current;
        }

        Console.WriteLine("\n=== SESSION REPORT ===");
        PrintPlayerReport(p1);
        PrintPlayerReport(p2);

        Console.WriteLine("\nWinner (Total IP): " + (p1.TotalIP > p2.TotalIP ? p1.Name : (p2.TotalIP > p1.TotalIP ? p2.Name : "TIE")));
        Console.WriteLine("\nDone.");
    }

    static void PrintPlayerReport(Player p)
    {
        Console.WriteLine($"\n{p.Name}");
        Console.WriteLine($"  Turns: {p.Turns}");
        Console.WriteLine($"  Total IP: {p.TotalIP}");
        Console.WriteLine($"  Avg IP/turn: {(p.Turns == 0 ? 0 : (double)p.TotalIP / p.Turns):0.00}");

        Console.WriteLine($"  Avg S/E/R: " +
                          $"{(p.Turns == 0 ? 0 : (double)p.SumS / p.Turns):0.00}/" +
                          $"{(p.Turns == 0 ? 0 : (double)p.SumE / p.Turns):0.00}/" +
                          $"{(p.Turns == 0 ? 0 : (double)p.SumR / p.Turns):0.00}");

        Console.WriteLine($"  Avg Clarity C: {(p.Turns == 0 ? 0 : p.SumC / p.Turns):0.00}");

        // 3-role “wheel” (derived)
        double sum = p.ArcheI + p.ArcheL + p.ArcheC;
        if (sum <= 1e-9) sum = 1;
        double i = p.ArcheI / sum;
        double l = p.ArcheL / sum;
        double c = p.ArcheC / sum;

        Console.WriteLine($"  Guild Lean (3-role):");
        Console.WriteLine($"    Initiator (Structure):  {i:0.00}  {Bar(i)}");
        Console.WriteLine($"    Listener  (Reciprocity):{l:0.00}  {Bar(l)}");
        Console.WriteLine($"    Challenger(Elevation):  {c:0.00}  {Bar(c)}");
        Console.WriteLine($"  Class: {ClassName3(i, l, c)}");
    }

    static string ClassName3(double i, double l, double c)
    {
        // simple top-2 naming
        var list = new List<(string n, double v)>
        {
            ("Initiator", i),
            ("Listener", l),
            ("Challenger", c)
        }.OrderByDescending(x => x.v).ToList();

        string a = list[0].n;
        string b = list[1].n;

        if ((a == "Listener" && b == "Initiator") || (a == "Initiator" && b == "Listener")) return "Harmonizer";
        if ((a == "Initiator" && b == "Challenger") || (a == "Challenger" && b == "Initiator")) return "Provocateur";
        if ((a == "Listener" && b == "Challenger") || (a == "Challenger" && b == "Listener")) return "Clarifier";
        return a;
    }

    static string Bar(double x)
    {
        int width = 20;
        int fill = (int)Math.Round(width * Clamp(x, 0.0, 1.0));
        return "[" + new string('█', fill) + new string('·', width - fill) + "]";
    }

    static string ReadNonEmpty(string prompt, string fallback)
    {
        Console.Write($"{prompt} [{fallback}]: ");
        string s = (Console.ReadLine() ?? "").Trim();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);
}