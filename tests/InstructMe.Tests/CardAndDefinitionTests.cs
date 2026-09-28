using System.Windows;
using InstructMe.Definitions;
using InstructMe.Overlay;

namespace InstructMe.Tests;

public class CardAndDefinitionTests
{
    private static readonly Size Screen = new(1920, 1080);
    private static readonly Size Card = new(400, 260);

    [Fact]
    public void Card_goes_right_of_the_sentence_when_there_is_room()
    {
        var sentence = new Rect(60, 280, 220, 40);
        var p = CardPlacement.Place(new Rect(200, 282, 50, 18), sentence, Card, Screen);
        Assert.True(p.X >= sentence.Right);
    }

    [Fact]
    public void Wide_subtitle_puts_the_card_above_or_below_without_covering_it()
    {
        var subtitle = new Rect(100, 900, 1720, 40);
        var p = CardPlacement.Place(new Rect(800, 905, 80, 30), subtitle, Card, Screen);
        var card = new Rect(p, Card);
        Assert.False(card.IntersectsWith(subtitle));
        Assert.True(card.Bottom <= Screen.Height - CardPlacement.Margin);
    }

    [Fact]
    public void Parses_the_model_json()
    {
        var d = WordDefinition.Parse("""
            {"word":"reach","lemma":"reach","partOfSpeech":"verbe","ipa":"/riːtʃ/",
             "translation":"atteindre","contextMeaning":"Ici : atteindre un niveau de satisfaction de 60."}
            """);
        Assert.Equal("atteindre", d.Translation);
        Assert.StartsWith("Ici :", d.ContextMeaning);
    }
}
