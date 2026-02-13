using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== CHATTER'S GUILD — CHALLENGE MODE ===");
        Console.WriteLine("XP earned only in Challenge Mode.");
        Console.WriteLine("Type 'end' to finish session.\n");

        Player adept = new Player("Adept");

        var recentTokens = new HashSet<string>();
        string lastMessage = "";

        int turn = 0;

        while (true)
        {
            Console.Write($"Turn {turn + 1}: ");
            string msg = Console.ReadLine();
            if (msg.ToLower() == "end")
                break;

            // Simulated scoring (replace with your AxesScorer if integrated)
            var result = AxesScorer.ScoreAxes(msg, lastMessage, lastMessage, recentTokens);
            int ip = AxesScorer.ComputeIP(result.S, result.E, result.R, result.C);

            adept.AddTurn(result.S, result.E, result.R, ip);

            Console.WriteLine($"  => S={result.S} E={result.E} R={result.R} | IP=+{ip}");

            AxesScorer.UpdateRecentTokens(recentTokens, msg);
            lastMessage = msg;
            turn++;
        }

        Console.WriteLine("\n=== SESSION COMPLETE ===");

        adept.FinalizeSession();

        adept.PrintPolygon();
        adept.AwardDisciplineXP();

        Console.WriteLine("\nDone.");
    }
}

class Player
{
    public string Name;

    int sumS, sumE, sumR;
    int turns;
    int totalIP;

    public Dictionary<string, Discipline> Disciplines;

    public Player(string name)
    {
        Name = name;
        Disciplines = Discipline.CreateDefaultDisciplines();
    }

    public void AddTurn(int S, int E, int R, int ip)
    {
        sumS += S;
        sumE += E;
        sumR += R;
        totalIP += ip;
        turns++;
    }

    public void FinalizeSession()
    {
        if (turns == 0) return;

        // Normalize into vector
        double I = (double)sumS / (turns * 10);
        double L = (double)sumR / (turns * 10);
        double C = (double)sumE / (turns * 10);

        double total = I + L + C;
        if (total == 0) total = 1;

        I /= total;
        L /= total;
        C /= total;

        SessionVector = new Vector3(I, L, C);

        Console.WriteLine($"Session Vector: I={I:0.00} L={L:0.00} C={C:0.00}");
    }

    public Vector3 SessionVector;

    public void PrintPolygon()
    {
        Console.WriteLine("\n--- POLYGON ---");
        Console.WriteLine($"Initiator  : {Bar(SessionVector.X)}");
        Console.WriteLine($"Listener   : {Bar(SessionVector.Y)}");
        Console.WriteLine($"Challenger : {Bar(SessionVector.Z)}");
    }

    public void AwardDisciplineXP()
    {
        Console.WriteLine("\n--- DISCIPLINE XP ---");

        foreach (var d in Disciplines.Values)
        {
            double similarity = Vector3.Cosine(SessionVector, d.Target);

            if (similarity > 0.80)
            {
                int xpGain = (int)(similarity * 20);
                d.AddXP(xpGain);

                Console.WriteLine($"{d.Name} +{xpGain} XP (Similarity {similarity:0.00})");
            }
        }
    }

    string Bar(double val)
    {
        int width = 20;
        int fill = (int)(val * width);
        return "[" + new string('█', fill) + new string('·', width - fill) + "]";
    }
}

class Discipline
{
    public string Name;
    public Vector3 Target;
    public int XP;
    public int Level;

    public Discipline(string name, Vector3 target)
    {
        Name = name;
        Target = target;
        XP = 0;
        Level = 1;
    }

    public void AddXP(int amount)
    {
        XP += amount;

        int threshold = Level * 100;
        if (XP >= threshold)
        {
            XP -= threshold;
            Level++;
            Console.WriteLine($"*** {Name} LEVEL UP! Now Level {Level} ***");
        }
    }

    public static Dictionary<string, Discipline> CreateDefaultDisciplines()
    {
        return new Dictionary<string, Discipline>
        {
            { "Medium", new Discipline("Medium", new Vector3(0.45,0.45,0.10)) },
            { "Sniper", new Discipline("Sniper", new Vector3(0.15,0.45,0.40)) },
            { "Provocateur", new Discipline("Provocateur", new Vector3(0.45,0.10,0.45)) },
            { "Harmonizer", new Discipline("Harmonizer", new Vector3(0.30,0.50,0.20)) },
            { "Clarifier", new Discipline("Clarifier", new Vector3(0.20,0.40,0.40)) },
            { "Architect", new Discipline("Architect", new Vector3(0.33,0.33,0.33)) }
        };
    }
}

struct Vector3
{
    public double X, Y, Z;

    public Vector3(double x, double y, double z)
    {
        X = x; Y = y; Z = z;
    }

    public static double Dot(Vector3 a, Vector3 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static double Magnitude(Vector3 v)
        => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    public static double Cosine(Vector3 a, Vector3 b)
    {
        double denom = Magnitude(a) * Magnitude(b);
        if (denom == 0) return 0;
        return Dot(a, b) / denom;
    }
}

// Minimal stub to keep example runnable if needed
static class AxesScorer
{
    public static (int S, int E, int R, double C) ScoreAxes(string m, string p, string l, HashSet<string> r)
    {
        int S = Math.Min(10, m.Length % 11);
        int E = Math.Min(10, (m.Length / 2) % 11);
        int R = Math.Min(10, (m.Length / 3) % 11);
        return (S, E, R, 1.0);
    }

    public static int ComputeIP(int S, int E, int R, double C)
        => (int)((0.45 * S + 0.35 * E + 0.20 * R) * 3);

    public static void UpdateRecentTokens(HashSet<string> r, string m) { }
}