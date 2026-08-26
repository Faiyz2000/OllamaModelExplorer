using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

public static class CategoryService
{
    public static void ApplyHeuristics(ModelInfo m)
    {
        var text = $"{m.Name} {m.Publisher} {m.Description} {m.Capabilities}".ToLowerInvariant();
        var cats = new List<string>();

        Add("Coding", "code", "coder", "coding", "codellama", "deepcoder", "devstral", "starcoder", "wizardcoder", "programming");
        Add("Reasoning", "reason", "reasoning", "deepseek-r1", "qwq", "thinking", "deepscaler");
        Add("Vision", "vision", "vl", "ocr", "visual");
        Add("Embedding", "embedding", "embed");
        Add("Translation", "translate", "translation", "translator", "translategemma");
        Add("Math", "math", "nsql");
        Add("Multilingual", "multilingual", "arabic");
        Add("Uncensored", "uncensored", "abliterated", "ablated", "heretic", "lexi");
        Add("Tools", "tools", "tool");
        Add("Audio", "audio", "speech");
        Add("Agentic", "agent", "function", "functiongemma");
        Add("Chat", "chat", "instruct", "assistant");

        m.CategoryText = string.Join(", ", cats.Distinct());

        void Add(string category, params string[] needles)
        {
            if (needles.Any(text.Contains))
                cats.Add(category);
        }
    }
}
