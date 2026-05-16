using System.Collections.Generic;
using System.Reflection;
using DiscordHass.Ui;
using Xunit;

namespace DiscordHass.Tests;

public class HelpContentTests
{
    [Fact]
    public void EveryTopicIdConstantHasACatalogEntry()
    {
        // Reflect every public const on HelpContent.TopicIds and assert it resolves.
        Type topics = typeof(HelpContent).GetNestedType("TopicIds", BindingFlags.Public | BindingFlags.NonPublic)!;
        FieldInfo[] consts = topics.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        List<string> failures = new();
        foreach (FieldInfo c in consts)
        {
            if (!c.IsLiteral) continue;
            string id = (string)c.GetRawConstantValue()!;
            try { _ = HelpContent.Get(id); }
            catch (KeyNotFoundException) { failures.Add(id); }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void Get_ThrowsForUnknownTopic()
    {
        Assert.Throws<KeyNotFoundException>(() => HelpContent.Get("nonexistent.topic"));
    }

    [Fact]
    public void EveryTopicHasNonEmptyTitleAndBody()
    {
        foreach (string id in HelpContent.AllTopicIds)
        {
            HelpTopic t = HelpContent.Get(id);
            Assert.False(string.IsNullOrWhiteSpace(t.Title), $"Topic '{id}' has empty Title");
            Assert.False(string.IsNullOrWhiteSpace(t.Body),  $"Topic '{id}' has empty Body");
        }
    }
}
