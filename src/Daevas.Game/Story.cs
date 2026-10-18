using System.Collections.Generic;

namespace Daevas.Game;

/// The campaign's story: why Daevas are on Sera, radio from COG Control between waves, and the endings.
/// All original writing for this mashup.
static class Story
{
    const string Control = "COG Control";
    const string Delta = "Delta, on the rooftops";

    public static readonly (string who, string line)[] Intro =
    {
        (Control, "Unknown signal in the Siege district. Something came through the sky over downtown."),
        (Control, "Visual confirmed: armoured soldiers with wings of light. They're not Locust, and they're not ours."),
        ("Daeva", "We crossed the Abyss chasing the Balaur. We found your war instead. Point us at the enemy."),
        (Control, "Then welcome to Emergence Day. The ground is opening all over the city. Hold this street."),
    };

    /// A briefing as each campaign wave starts (index = wave - 1).
    public static readonly (string who, string line)[][] Briefings =
    {
        new[] { (Control, "Ground's cracking on your street. Wretches first; they're fast and they're everywhere.") },
        new[] { (Delta, "Drones with rifles behind the wrecks. Close the gap or burn them out."), (Control, "Watch for Tickers. Don't let them get close.") },
        new[] { (Control, "Big seismic reading under you. Something heavy is coming up."), (Delta, "That's a Vrak. Stay out of its slam and hit it while it recovers.") },
        new[] { (Control, "A swarm is surfacing across the district."), (Delta, "Reaver overhead! Bring it down when it swoops.") },
        new[] { (Delta, "Snipers on the overpass. When you see the red line, move."), (Control, "There's a Spore Matron healing them. Kill the healer first.") },
        new[] { (Control, "The tunnels under the Siege are collapsing. Something enormous is digging."), (Delta, "Corpser! When the ground glows red under you, get clear.") },
        new[] { (Control, "Heat signatures off the scale. Vrak the Scorched leads this push."), (Delta, "Fire everywhere. Don't stand in it.") },
        new[] { (Control, "This is it. A Brumak is walking up the main street."), ("Daeva", "Then it ends here.") },
    };

    public static readonly (string who, string line)[] Ending =
    {
        (Control, "The Brumak is down. The Locust are pulling back into the holes."),
        (Delta, "Never seen anyone fly like that. Whoever you are, you saved this district."),
        ("Daeva", "The Abyss is still open behind us. When the Locust come back, so will we."),
    };

    public static readonly (string who, string line)[] SecretEnding =
    {
        (Control, "One life, no second chances, and you broke the Siege anyway."),
        ("Daeva", "In Atreia they'd call that Ascension. Here, I'll settle for a quiet street."),
        (Control, "Command is calling you the Ascended of Sera. The name's yours."),
    };

    public static string Epilogue(bool won, bool hardcore) =>
        !won ? "The Siege holds for now. The Daevas regroup at the edge of the Abyss."
        : hardcore ? "Secret ending: the Ascended of Sera. One life, every wave, and the Brumak at your feet."
        : "The district is quiet. Somewhere below, the Locust Queen is already planning the next Emergence.";

    /// Short lore entries for the codex (each Locust type) and the class bios.
    public static readonly Dictionary<string, string> Codex = new()
    {
        ["wretch"] = "Pale and starving, Wretches pour out first and climb anything. They die fast but never come alone.",
        ["drone"] = "The Locust foot soldier: disciplined, armed, and happy to sit behind cover and wear you down.",
        ["boomer"] = "A walking artillery piece. When it shouts, get out of the way of what comes next.",
        ["ticker"] = "A bomb on legs. It runs at you and explodes; kill it far away, or near its friends.",
        ["reaver"] = "A flying beast that spits bile from the rooftops. It has to come down to the street to fight close.",
        ["wretch-armored"] = "A Wretch in plates of bone. Lightning finds the gaps; blades mostly don't.",
        ["drone-sniper"] = "A marksman with a Breechshot. The red laser is your warning, and your cue to move.",
        ["drone-shotgun"] = "An assault Drone that wants to be in your face. Keep your distance.",
        ["grenadier"] = "It lobs frag grenades where you're standing. Watch the ground for the red circle.",
        ["flooder"] = "A crawling sack of acid. Its spit burns for a while after it lands.",
        ["sporematron"] = "A floating mother of spores that mends other Locust. Kill it first.",
        ["seeder"] = "Rooted in the street, it fires Nemacysts that float after you and burst.",
        ["nemacyst"] = "A drifting bomb launched by a Seeder. Shoot it before it reaches you.",
        ["broodshifter"] = "It sweeps a beam across the street from above. Step out of the line.",
        ["vrak"] = "A Locust champion with a harpoon and a temper. Its slams can be dodged; its anger can't.",
        ["vrak-scorched"] = "The Vrak who burned his way up from the deepest tunnels. Everything near him catches fire.",
        ["corpser"] = "A giant burrowing horror. It vanishes below and comes up under whatever it hates most.",
        ["brumak"] = "The beast of the Hollow, built like a tank with guns bolted on. The end of every Locust offensive.",
        ["dummy"] = "A COG training target. It doesn't fight back.",
    };

    /// Lore pages found in the streets (original writing for this mashup).
    public static readonly (string title, string text)[] Lore =
    {
        ("The Rift Over Sera", "The tear in the sky opened the night the Abyss shuddered. What came through were not Balaur, but Daevas, chasing them across worlds."),
        ("A Soldier's Note", "Saw one fly today. Wings like light off a blade. It cut through a Boomer like the thing was paper. Command says don't shoot them. Fine by me."),
        ("Field Report: Ascension", "The visitors call it Ascension: the moment a mortal becomes a Daeva. They say it cannot be taught, only survived."),
        ("Locust Biology, Page 3", "Wretches lose their pigment from living without sunlight. They are fast, starved and stupid, and there are always more of them."),
        ("Kinah", "The Daevas trade in a currency called Kinah. Our quartermaster has started accepting it. No one knows where they keep it."),
        ("The Brumak Problem", "Nothing we field can stop a Brumak head on. Air support, close support, prayer. The Daevas suggested 'a lot of hitting'."),
        ("Imulsion Notes", "Imulsion crystals glow in the tunnels where the Locust dig. The Daevas use them to craft. Our scientists would like a word."),
        ("The Hollow", "Below Sera lies the Hollow, a world of tunnels. The Locust never stop digging. Every E-hole is a door they open from below."),
        ("Manastones", "A Daeva's armour carries little stones that hum when you hold them near. They fit into slots no human smith would think to cut."),
        ("Night Rules", "Stay in the light after dark. The Kryll take anything that stands in the open. Even the Daevas don't argue with that rule."),
        ("Stigma", "Each Daeva carries marks called Stigmas, burned in at hard-won moments. They say each one changes how a Daeva fights."),
        ("Last Entry", "If you find this, the Siege held. Tell them the street held. Tell them we weren't alone."),
    };

    /// Named places in the Siege district, read out by COG Control when you reach them (original, fictional).
    public static readonly (string name, string about)[] Landmarks =
    {
        ("Founders' Plaza", "Founders' Plaza: the old market square. Locust came up through the fountain on the first day."),
        ("The Rail Cut", "The Rail Cut: the elevated line crossed here. Half of it is in the street now."),
        ("Steelworks Gate", "Steelworks Gate: the foundries supplied the COG for forty years. Nothing's moved in there since the first night."),
        ("Saint Ambrose Steps", "Saint Ambrose Steps: refugees sheltered in the church. Hold this ground and they get out."),
        ("Courier Row", "Courier Row: the old merchants' street. Watch the windows, snipers love the upper floors."),
        ("The Old Clocktower", "The Old Clocktower: still keeping time. Someone in there keeps winding it."),
    };

    /// A line per hero for the class select (original).
    public static readonly Dictionary<string, string> Quotes = new()
    {
        ["marcus"] = "\"Wings or no wings, a Locust is a Locust.\"",
        ["dom"] = "\"I've seen stranger things. Not many, but some.\"",
        ["carmine"] = "\"Keep your head down and your shield up.\"",
        ["mags"] = "\"Quiet, quick, done. That's the job.\"",
        ["lucas"] = "\"I'll take the high ground. You take the rest.\"",
        ["gill"] = "\"Fire solves most problems. Frost solves the rest.\"",
        ["medic"] = "\"Nobody dies on my street. Not today.\"",
        ["tai"] = "\"Breathe. Strike. Breathe again.\"",
    };

    public static readonly Dictionary<string, string> Bios = new()
    {
        ["gladiator"] = "Gladiators fight up close with huge weapons. On Sera they found the Locust very easy to hit.",
        ["templar"] = "Templars hold the line with shield and faith. The COG respects anyone who stands their ground.",
        ["assassin"] = "Assassins strike from the shadows with poison and runes. Wretches never see them coming.",
        ["ranger"] = "Rangers rain arrows from range and lay traps. They took to Sera's rooftops at once.",
        ["sorcerer"] = "Sorcerers bend fire and frost. Their Meteor is the loudest thing the Siege has heard.",
        ["spiritmaster"] = "Spiritmasters call elemental spirits to fight beside them and curse what they can't reach.",
        ["cleric"] = "Clerics heal, shield and smite with holy light. One Cleric keeps a street alive.",
        ["chanter"] = "Chanters fight with a staff and mantras that make them faster and harder to kill.",
    };
}
