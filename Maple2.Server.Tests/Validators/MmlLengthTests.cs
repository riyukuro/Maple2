using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

// The length the client's score writer sends with a compose (UIWriteMusicDialog::OnClickMake, MMLParser::Parse,
// CInstrumentManager::ParseLine): 60000 / tempo / length * 4 ms a note, 1.5 times dotted, from tempo 120 and length 4.
public class MmlLengthTests {
    [TestCase("c", 500)]
    [TestCase("cdefg", 2500)]
    [TestCase("C+D-e", 1500)] // sharps, flats and capitals
    [TestCase("c8", 250)]
    [TestCase("c4.", 750)]
    [TestCase("l2c", 1000)]
    [TestCase("l4.c", 750)]
    [TestCase("l4.c8", 250)] // a note's own length is not dotted by the L
    [TestCase("t60c", 1000)]
    [TestCase("t300c", 500)] // a tempo over 255 is ignored
    [TestCase("t31c", 500)] // and one under 32
    [TestCase("t32l1.c", 11250)] // the slowest note
    [TestCase("r1", 2000)] // a rest takes its time
    [TestCase("n60", 500)] // a note by number takes the default length
    [TestCase("n200", 0)] // out of range: no note
    [TestCase("o2c>c<<c", 1500)] // octaves change no time
    [TestCase("v15cv0c", 1000)]
    [TestCase("c0", 0)] // length 0: no time
    [TestCase("c9999", 0)] // a note of 1 ms or less takes no time
    [TestCase("r9999r9999r9999r9999", 0)] // a rest does (0.2 ms each), truncated
    [TestCase("r9999r9999r9999r9999r9999", 1)]
    [TestCase("c9999&c9999&c9999&c9999&c9999&c9999", 1)] // tied into one note of 1.2 ms
    [TestCase("c&c", 1000)]
    [TestCase("c&d", 1000)]
    [TestCase("s1c y7,100c i0,64c", 1500)] // pedal, control and pitch bend take no time
    [TestCase("m1p100c", 500)] // motion events take none, and P sets no tempo
    [TestCase("c99999999999", 0)] // a length past int.MaxValue stops there
    [TestCase("xyz?", 0)]
    [TestCase("", 0)]
    public void OneTab(string mml, long expected) {
        Assert.That(MmlLength.Of(mml, 0), Is.EqualTo(expected));
    }

    // The score lasts as long as its longest tab; with a percussion instrument only the first tab plays.
    [TestCase("cdef:cdefgabc", 0, 4000)]
    [TestCase("cdefgabc:cd", 0, 4000)]
    [TestCase("cdef:cdefgabc", 35, 2000)]
    [TestCase("m1cdef:m1cd", 0, 2000)] // a motion event on a later tab takes no time either
    public void Tabs(string mml, int percussionId, long expected) {
        Assert.That(MmlLength.Of(mml, percussionId), Is.EqualTo(expected));
    }
}
