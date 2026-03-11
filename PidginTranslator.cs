using System.Text.RegularExpressions;

namespace wetin_happen_dem_internets
{
    /// <summary>
    /// Two-pass EN → Nigerian Pidgin translator.
    /// Pass 1: structural rule transforms (grammar + expanded vocabulary).
    /// Pass 2: optional goldfish-model fluency smoother.
    /// </summary>
    internal static class PidginTranslator
    {
        // ─── Public entry point ─────────────────────────────────────────────────────

        public static string Translate(string english)
        {
            if (string.IsNullOrWhiteSpace(english)) return english;

            var text = ApplyProgressiveTense(english);
            text = ApplyPossessives(text);
            text = ApplyPhrasePatterns(text);
            text = ApplyWordSubstitution(text);
            text = FixupPunctuation(text);
            return text;
        }

        // ── Progressive tense: is/are/was/were + -ing → dey/bin dey + base ─────────

        private static string ApplyProgressiveTense(string text)
        {
            text = Regex.Replace(text, @"\b(?:is|are)\s+(\w+ing)\b",
                m => "dey " + StripIng(m.Groups[1].Value), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:was|were)\s+(\w+ing)\b",
                m => "bin dey " + StripIng(m.Groups[1].Value), RegexOptions.IgnoreCase);
            return text;
        }

        private static string StripIng(string word)
        {
            if (word.Length < 5 || !word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
                return word;

            var stem = word[..^3];

            // doubled consonant: running→run, sitting→sit
            if (stem.Length >= 2 && !IsVowel(stem[^1]) && stem[^1] == stem[^2])
                return stem[..^1];

            // silent-e drop: making→make, having→have, coming→come
            if (stem.Length >= 3 && !IsVowel(stem[^1]))
                return stem + "e";

            return stem.Length >= 3 ? stem : word;
        }

        private static bool IsVowel(char c) => "aeiou".Contains(char.ToLower(c));

        // ── Possessives: Nigeria's → Nigeria im ─────────────────────────────────────

        private static string ApplyPossessives(string text) =>
            Regex.Replace(text, @"(\w+)'s\b", "$1 im", RegexOptions.IgnoreCase);

        // ── Phrase patterns ──────────────────────────────────────────────────────────

        private static string ApplyPhrasePatterns(string text)
        {
            foreach (var (pattern, replacement) in PhrasePatterns)
                text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
            return text;
        }

        private static readonly (string Pattern, string Replacement)[] PhrasePatterns =
        [
            // Perfect aspect
            (@"\b(has|have)\s+been\b",      "don dey"),
            (@"\bhad\s+been\b",             "bin dey"),
            (@"\b(has|have)\s+(\w+ed)\b",   "don $2"),
            (@"\bhad\s+(\w+ed)\b",          "bin $2"),

            // Future / modal
            (@"\b(will|shall)\s+be\b",          "go be"),
            (@"\b(will|shall)\s+not\s+(\w+)\b", "no go $2"),
            (@"\bwill\s+(\w+)\b",               "go $1"),
            (@"\bshall\s+(\w+)\b",              "go $1"),
            (@"\bwould\s+(\w+)\b",              "go $1"),
            (@"\bcould\s+(\w+)\b",              "fit $1"),
            (@"\bshould\s+(\w+)\b",             "suppose to $1"),
            (@"\bmust\s+(\w+)\b",               "must $1"),
            (@"\bcan\s+(\w+)\b",                "fit $1"),
            (@"\bcannot\s+(\w+)\b",             "no fit $1"),
            (@"\bcan't\s+(\w+)\b",              "no fit $1"),
            (@"\bwon't\s+(\w+)\b",              "no go $1"),

            // Negation
            (@"\bdo\s+not\s+(\w+)\b",       "no $1"),
            (@"\bdoes\s+not\s+(\w+)\b",     "no $1"),
            (@"\bdid\s+not\s+(\w+)\b",      "no $1"),
            (@"\bdon't\s+(\w+)\b",          "no $1"),
            (@"\bdoesn't\s+(\w+)\b",        "no $1"),
            (@"\bdidn't\s+(\w+)\b",         "no $1"),
            (@"\bisn't\b",                  "no be"),
            (@"\baren't\b",                 "no dey"),
            (@"\bwasn't\b",                 "no bin be"),
            (@"\bweren't\b",                "no bin dey"),
            (@"\bhasn't\s+(\w+)\b",         "never $1"),
            (@"\bhaven't\s+(\w+)\b",        "never $1"),

            // Copula / existential
            (@"\bthere\s+(is|are|was|were)\b",  "e dey"),
            (@"\bit\s+is\b",                    "e na"),
            (@"\bit\s+was\b",                   "e bin na"),
            (@"\bthis\s+is\b",                  "dis na"),
            (@"\bthat\s+is\b",                  "dat na"),
            (@"\bthey\s+are\b",                 "dem dey"),
            (@"\bthey\s+were\b",                "dem bin dey"),
            (@"\bwe\s+are\b",                   "we dey"),
            (@"\bwe\s+were\b",                  "we bin dey"),
            (@"\byou\s+are\b",                  "you dey"),
            (@"\byou\s+were\b",                 "you bin dey"),
            (@"\bhe\s+is\b",                    "im dey"),
            (@"\bshe\s+is\b",                   "im dey"),
            (@"\bhe\s+was\b",                   "im bin dey"),
            (@"\bshe\s+was\b",                  "im bin dey"),
            (@"\bi\s+am\b",                     "I dey"),
            (@"\bi\s+was\b",                    "I bin"),

            // Quantity
            (@"\ba\s+lot\s+of\b",               "plenti"),
            (@"\blots\s+of\b",                  "plenti"),
            (@"\bvery\s+much\b",                "well well"),
            (@"\bso\s+much\b",                  "well well"),
            (@"\bright\s+now\b",                "now now"),
            (@"\bjust\s+now\b",                 "just now"),
            (@"\bonce\s+again\b",               "one more time"),
            (@"\ball\s+over\s+the\b",           "for everywhere for"),
            (@"\bin\s+order\s+to\b",            "so as to"),
            (@"\bas\s+well\s+as\b",             "and also"),
            (@"\bas\s+a\s+result\b",            "so"),
            (@"\bat\s+the\s+same\s+time\b",     "for same time"),
            (@"\bon\s+the\s+other\s+hand\b",    "but"),
            (@"\bin\s+addition\b",              "plus dat"),
            (@"\bin\s+fact\b",                  "true true"),
            (@"\bof\s+course\b",                "of course na"),
            (@"\baccording\s+to\b",             "as"),
            (@"\bdue\s+to\b",                   "because of"),
            (@"\bin\s+spite\s+of\b",            "even though"),
            (@"\bmore\s+and\s+more\b",          "more more"),
            (@"\bover\s+and\s+over\b",          "again and again"),
            (@"\bday\s+by\s+day\b",             "day by day"),

            // Reporting verbs + "that"
            (@"\bsaid\s+that\b",        "tok say"),
            (@"\bstated\s+that\b",      "tok say"),
            (@"\bclaimed\s+that\b",     "claim say"),
            (@"\bnoted\s+that\b",       "note say"),
            (@"\badded\s+that\b",       "also tok say"),
            (@"\bwarned\s+that\b",      "warn say"),
            (@"\bsuggested\s+that\b",   "suggest say"),
            (@"\bannounced\s+that\b",   "announce say"),
            (@"\breported\s+that\b",    "report say"),
            (@"\bconfirmed\s+that\b",   "confirm say"),
            (@"\bdenied\s+that\b",      "deny say"),
            (@"\bbelieved\s+that\b",    "believe say"),
            (@"\bexpected\s+that\b",    "expect say"),
            (@"\bshowed\s+that\b",      "show say"),
            (@"\bfound\s+that\b",       "find say"),
            (@"\bsays\s+that\b",        "tok say"),

            // Common verb phrases
            (@"\btook\s+place\b",       "happen"),
            (@"\bcarried\s+out\b",      "do"),
            (@"\bcalled\s+for\b",       "ask for"),
            (@"\bpointed\s+out\b",      "talk about"),
            (@"\bbrought\s+up\b",       "raise"),
            (@"\bcame\s+out\b",         "come out"),
            (@"\bfound\s+out\b",        "find out"),
            (@"\bmade\s+clear\b",       "make am clear"),
            (@"\bgave\s+up\b",          "give up"),
            (@"\bput\s+forward\b",      "suggest"),
            (@"\bset\s+out\b",          "plan to"),
            (@"\bset\s+up\b",           "set up"),

            // Adversatives / connectives
            (@"\bhowever\b",            "but"),
            (@"\btherefore\b",          "so"),
            (@"\bnevertheless\b",       "still still"),
            (@"\bfurthermore\b",        "also"),
            (@"\bmoreover\b",           "also"),
            (@"\balthough\b",           "even though"),
            (@"\bwhereas\b",            "while"),
            (@"\bunless\b",             "except if"),
            (@"\bregardless\b",         "no matter wetin"),
            (@"\bperhaps\b",            "maybe"),
            (@"\bpossibly\b",           "maybe"),
        ];

        // ── Word substitution ────────────────────────────────────────────────────────

        private static string ApplyWordSubstitution(string text) =>
            Regex.Replace(text, @"\b\w+\b", m =>
            {
                var word = m.Value;
                return WordMap.TryGetValue(word.ToLowerInvariant(), out var pidgin)
                    ? PreserveCase(word, pidgin)
                    : word;
            });

        private static string PreserveCase(string source, string target)
        {
            if (string.IsNullOrEmpty(target)) return target;
            if (source.All(char.IsUpper)) return target.ToUpperInvariant();
            if (char.IsUpper(source[0])) return char.ToUpperInvariant(target[0]) + target[1..];
            return target;
        }

        private static string FixupPunctuation(string text)
        {
            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");
            text = Regex.Replace(text, @"\s{2,}", " ");
            return text.Trim();
        }

        // ── Vocabulary map ───────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> WordMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Pronouns
            ["i"]           = "I",
            ["me"]          = "me",
            ["my"]          = "my",
            ["mine"]        = "my own",
            ["myself"]      = "myself",
            ["you"]         = "you",
            ["your"]        = "your",
            ["yours"]       = "your own",
            ["yourself"]    = "yourself",
            ["he"]          = "im",
            ["she"]         = "im",
            ["him"]         = "im",
            ["her"]         = "im",
            ["his"]         = "im",
            ["himself"]     = "imself",
            ["herself"]     = "imself",
            ["it"]          = "e",
            ["its"]         = "im",
            ["itself"]      = "imself",
            ["we"]          = "we",
            ["us"]          = "us",
            ["our"]         = "our",
            ["ours"]        = "our own",
            ["they"]        = "dem",
            ["them"]        = "dem",
            ["their"]       = "dem",
            ["theirs"]      = "dem own",
            ["themselves"]  = "demself",

            // Articles / determiners  (note: a/an intentionally NOT replaced)
            ["the"]         = "di",
            ["this"]        = "dis",
            ["that"]        = "dat",
            ["these"]       = "dis ones",
            ["those"]       = "dem ones",
            ["every"]       = "every",
            ["each"]        = "each",
            ["both"]        = "both",
            ["all"]         = "all",

            // Conjunctions
            ["and"]         = "and",
            ["but"]         = "but",
            ["or"]          = "or",
            ["so"]          = "so",
            ["because"]     = "because",
            ["since"]       = "since",
            ["while"]       = "while",
            ["when"]        = "when",
            ["if"]          = "if",
            ["until"]       = "until",
            ["before"]      = "before",
            ["after"]       = "after",
            ["whether"]     = "whether",

            // Prepositions
            ["in"]          = "for",
            ["at"]          = "for",
            ["on"]          = "on",
            ["by"]          = "by",
            ["with"]        = "wit",
            ["about"]       = "about",
            ["against"]     = "against",
            ["between"]     = "between",
            ["through"]     = "through",
            ["during"]      = "during",
            ["without"]     = "without",
            ["within"]      = "inside",
            ["around"]      = "around",
            ["among"]       = "among",
            ["across"]      = "across",
            ["behind"]      = "behind",
            ["below"]       = "below",
            ["above"]       = "above",
            ["under"]       = "under",
            ["over"]        = "over",
            ["near"]        = "near",
            ["into"]        = "into",
            ["from"]        = "from",
            ["of"]          = "of",
            ["to"]          = "to",
            ["towards"]     = "towards",
            ["onto"]        = "onto",
            ["upon"]        = "on",
            ["off"]         = "comot from",

            // Copula
            ["is"]          = "na",
            ["are"]         = "dey",
            ["was"]         = "bin",
            ["were"]        = "bin dey",
            ["be"]          = "be",
            ["been"]        = "dey",
            ["am"]          = "dey",

            // Aux verbs (standalone, after phrase-level pass)
            ["have"]        = "get",
            ["has"]         = "get",
            ["had"]         = "bin get",
            ["do"]          = "do",
            ["does"]        = "do",
            ["did"]         = "do",

            // High-frequency verbs
            ["go"]          = "go",
            ["goes"]        = "go",
            ["went"]        = "go",
            ["gone"]        = "go",
            ["come"]        = "come",
            ["came"]        = "come",
            ["get"]         = "get",
            ["got"]         = "get",
            ["give"]        = "give",
            ["gave"]        = "give",
            ["given"]       = "give",
            ["put"]         = "put",
            ["keep"]        = "keep",
            ["kept"]        = "keep",
            ["let"]         = "let",
            ["make"]        = "make",
            ["made"]        = "make",
            ["take"]        = "take",
            ["took"]        = "take",
            ["taken"]       = "take",
            ["bring"]       = "bring",
            ["brought"]     = "bring",
            ["say"]         = "tok",
            ["said"]        = "tok",
            ["says"]        = "tok",
            ["tell"]        = "tell",
            ["told"]        = "tell",
            ["speak"]       = "talk",
            ["spoke"]       = "talk",
            ["talk"]        = "talk",
            ["talked"]      = "talk",
            ["know"]        = "know",
            ["knew"]        = "know",
            ["think"]       = "think",
            ["thought"]     = "think",
            ["believe"]     = "believe",
            ["feel"]        = "feel",
            ["felt"]        = "feel",
            ["want"]        = "want",
            ["need"]        = "need",
            ["like"]        = "like",
            ["love"]        = "love",
            ["hate"]        = "hate",
            ["see"]         = "see",
            ["saw"]         = "see",
            ["seen"]        = "see",
            ["look"]        = "look",
            ["hear"]        = "hear",
            ["heard"]       = "hear",
            ["find"]        = "find",
            ["found"]       = "find",
            ["use"]         = "use",
            ["used"]        = "use",
            ["help"]        = "help",
            ["helped"]      = "help",
            ["call"]        = "call",
            ["called"]      = "call",
            ["show"]        = "show",
            ["showed"]      = "show",
            ["shown"]       = "show",
            ["ask"]         = "ask",
            ["asked"]       = "ask",
            ["try"]         = "try",
            ["tried"]       = "try",
            ["run"]         = "run",
            ["ran"]         = "run",
            ["walk"]        = "waka",
            ["walked"]      = "waka",
            ["leave"]       = "comot",
            ["left"]        = "comot",
            ["return"]      = "come back",
            ["returned"]    = "come back",
            ["begin"]       = "start",
            ["began"]       = "start",
            ["start"]       = "start",
            ["started"]     = "start",
            ["stop"]        = "stop",
            ["stopped"]     = "stop",
            ["finish"]      = "finish",
            ["finished"]    = "finish",
            ["open"]        = "open",
            ["opened"]      = "open",
            ["close"]       = "close",
            ["closed"]      = "close",
            ["build"]       = "build",
            ["built"]       = "build",
            ["buy"]         = "buy",
            ["bought"]      = "buy",
            ["sell"]        = "sell",
            ["sold"]        = "sell",
            ["pay"]         = "pay",
            ["paid"]        = "pay",
            ["send"]        = "send",
            ["sent"]        = "send",
            ["receive"]     = "receive",
            ["received"]    = "receive",
            ["win"]         = "win",
            ["won"]         = "win",
            ["lose"]        = "lose",
            ["lost"]        = "lose",
            ["fall"]        = "fall",
            ["fell"]        = "fall",
            ["grow"]        = "grow",
            ["grew"]        = "grow",
            ["cut"]         = "cut",
            ["hit"]         = "hit",
            ["kill"]        = "kill",
            ["killed"]      = "kill",
            ["die"]         = "die",
            ["died"]        = "die",
            ["live"]        = "live",
            ["work"]        = "work",
            ["worked"]      = "work",
            ["eat"]         = "chop",
            ["ate"]         = "chop",
            ["eaten"]       = "chop",
            ["drink"]       = "drink",
            ["drank"]       = "drink",
            ["sleep"]       = "sleep",
            ["slept"]       = "sleep",
            ["enter"]       = "enter",
            ["entered"]     = "enter",
            ["join"]        = "join",
            ["joined"]      = "join",
            ["meet"]        = "meet",
            ["met"]         = "meet",
            ["create"]      = "create",
            ["created"]     = "create",
            ["develop"]     = "develop",
            ["developed"]   = "develop",
            ["increase"]    = "increase",
            ["increased"]   = "increase",
            ["decrease"]    = "decrease",
            ["decreased"]   = "decrease",
            ["reduce"]      = "reduce",
            ["reduced"]     = "reduce",
            ["raise"]       = "raise",
            ["raised"]      = "raise",
            ["rise"]        = "rise",
            ["rose"]        = "rise",
            ["spread"]      = "spread",
            ["fight"]       = "fight",
            ["fought"]      = "fight",
            ["support"]     = "support",
            ["supported"]   = "support",
            ["oppose"]      = "oppose",
            ["opposed"]     = "oppose",
            ["agree"]       = "agree",
            ["agreed"]      = "agree",
            ["discuss"]     = "discuss",
            ["discussed"]   = "discuss",
            ["decide"]      = "decide",
            ["decided"]     = "decide",
            ["plan"]        = "plan",
            ["planned"]     = "plan",
            ["report"]      = "report",
            ["reported"]    = "report",
            ["announce"]    = "announce",
            ["announced"]   = "announce",
            ["confirm"]     = "confirm",
            ["confirmed"]   = "confirm",
            ["deny"]        = "deny",
            ["denied"]      = "deny",
            ["release"]     = "release",
            ["released"]    = "release",
            ["launch"]      = "launch",
            ["launched"]    = "launch",
            ["study"]       = "study",
            ["studied"]     = "study",
            ["learn"]       = "learn",
            ["learned"]     = "learn",
            ["teach"]       = "teach",
            ["taught"]      = "teach",
            ["read"]        = "read",
            ["write"]       = "write",
            ["wrote"]       = "write",
            ["written"]     = "write",
            ["improve"]     = "improve",
            ["improved"]    = "improve",
            ["change"]      = "change",
            ["changed"]     = "change",
            ["become"]      = "turn",
            ["became"]      = "turn",
            ["describe"]    = "describe",

            // Adjectives
            ["new"]         = "new",
            ["old"]         = "old",
            ["big"]         = "big",
            ["large"]       = "big",
            ["small"]       = "small",
            ["little"]      = "small small",
            ["good"]        = "good",
            ["bad"]         = "bad",
            ["great"]       = "great",
            ["high"]        = "high",
            ["low"]         = "low",
            ["long"]        = "long",
            ["short"]       = "short",
            ["fast"]        = "fast",
            ["slow"]        = "slow",
            ["hard"]        = "hard",
            ["easy"]        = "easy",
            ["strong"]      = "strong",
            ["weak"]        = "weak",
            ["rich"]        = "rich",
            ["poor"]        = "poor",
            ["hot"]         = "hot",
            ["cold"]        = "cold",
            ["young"]       = "young",
            ["important"]   = "important",
            ["serious"]     = "serious",
            ["different"]   = "different",
            ["same"]        = "same",
            ["true"]        = "true",
            ["false"]       = "fake",
            ["right"]       = "correct",
            ["wrong"]       = "wrong",
            ["free"]        = "free",
            ["major"]       = "big",
            ["minor"]       = "small",
            ["recent"]      = "recent",
            ["current"]     = "current",
            ["former"]      = "former",
            ["late"]        = "late",
            ["early"]       = "early",
            ["main"]        = "main",
            ["key"]         = "main",
            ["total"]       = "total",
            ["full"]        = "full",
            ["whole"]       = "whole",
            ["final"]       = "final",
            ["last"]        = "last",
            ["first"]       = "first",
            ["next"]        = "next",
            ["previous"]    = "before",
            ["real"]        = "real",
            ["successful"]  = "successful",
            ["significant"] = "big",
            ["similar"]     = "similar",
            ["additional"]  = "extra",
            ["multiple"]    = "many",
            ["various"]     = "different",
            ["several"]     = "many",
            ["certain"]     = "certain",

            // Adverbs
            ["not"]         = "no",
            ["never"]       = "never",
            ["always"]      = "always",
            ["often"]       = "often",
            ["usually"]     = "usually",
            ["sometimes"]   = "sometimes",
            ["recently"]    = "recently",
            ["currently"]   = "now",
            ["previously"]  = "before",
            ["finally"]     = "finally",
            ["also"]        = "also",
            ["only"]        = "only",
            ["even"]        = "even",
            ["just"]        = "just",
            ["more"]        = "more",
            ["less"]        = "less",
            ["most"]        = "most",
            ["least"]       = "least",
            ["much"]        = "plenti",
            ["many"]        = "plenti",
            ["few"]         = "small small",
            ["very"]        = "very",
            ["too"]         = "too",
            ["enough"]      = "enough",
            ["nearly"]      = "nearly",
            ["almost"]      = "nearly",
            ["already"]     = "don already",
            ["still"]       = "still",
            ["again"]       = "again",
            ["here"]        = "here",
            ["there"]       = "there",
            ["really"]      = "true true",
            ["quickly"]     = "sharp sharp",
            ["slowly"]      = "slow slow",
            ["together"]    = "together",
            ["away"]        = "comot",
            ["back"]        = "back",

            // Question words
            ["what"]        = "wetin",
            ["where"]       = "where",
            ["how"]         = "how",
            ["why"]         = "why",
            ["who"]         = "who",
            ["which"]       = "which",
            ["when"]        = "when",

            // Indefinite pronouns
            ["nothing"]     = "notin",
            ["something"]   = "sometin",
            ["everything"]  = "everytin",
            ["anything"]    = "anytin",
            ["someone"]     = "somebody",
            ["anyone"]      = "anybody",
            ["everyone"]    = "everybody",
            ["nobody"]      = "nobody",
            ["somewhere"]   = "somewhere",
            ["anywhere"]    = "anywhere",
            ["everywhere"]  = "everywhere",
            ["nowhere"]     = "nowhere",

            // News-domain nouns
            ["government"]  = "goment",
            ["people"]      = "pipul",
            ["person"]      = "person",
            ["man"]         = "man",
            ["woman"]       = "woman",
            ["child"]       = "pikin",
            ["children"]    = "pikin dem",
            ["family"]      = "family",
            ["community"]   = "community",
            ["citizens"]    = "citizens",
            ["workers"]     = "workers",
            ["boss"]        = "oga",
            ["problem"]     = "wahala",
            ["issue"]       = "wahala",
            ["challenge"]   = "wahala",
            ["conflict"]    = "wahala",
            ["trouble"]     = "wahala",
            ["crisis"]      = "crisis",
            ["attack"]      = "attack",
            ["war"]         = "war",
            ["peace"]       = "peace",
            ["money"]       = "money",
            ["food"]        = "food",
            ["water"]       = "water",
            ["home"]        = "house",
            ["house"]       = "house",
            ["hospital"]    = "hospital",
            ["school"]      = "school",
            ["news"]        = "news",
            ["story"]       = "story",
            ["report"]      = "report",
            ["decision"]    = "decision",
            ["plan"]        = "plan",
            ["health"]      = "health",
            ["disease"]     = "sickness",
            ["virus"]       = "virus",
            ["death"]       = "death",
            ["life"]        = "life",
            ["year"]        = "year",
            ["years"]       = "years",
            ["month"]       = "month",
            ["week"]        = "week",
            ["day"]         = "day",
            ["today"]       = "today",
            ["yesterday"]   = "yesterday",
            ["tomorrow"]    = "tomorrow",
            ["now"]         = "now",
            ["time"]        = "time",
            ["awesome"]      = "ogbonge",
            ["excellent"]    = "ogbonge",
            ["fantastic"]    = "ogbonge",
            ["outstanding"]  = "ogbonge",
            ["remarkable"]   = "ogbonge",
            ["incredible"]   = "ogbonge",
            ["extraordinary"] = "ogbonge",
            ["exceptional"]  = "ogbonge",
            ["magnificent"]  = "ogbonge",
            ["brilliant"]    = "ogbonge",
            ["superb"]       = "ogbonge",
            ["wonderful"]    = "ogbonge",
            ["phenomenal"]   = "ogbonge",
            ["spectacular"]  = "ogbonge",
            ["cool"]         = "ogbonge",
            ["amazing"]      = "ogbonge",
            ["impressive"]   = "ogbonge",
            ["stunning"]     = "ogbonge",
        };
    }
}
