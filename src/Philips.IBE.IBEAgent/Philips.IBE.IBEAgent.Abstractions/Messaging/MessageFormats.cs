namespace Philips.IBE.IBEAgent.Abstractions;

// Well-known Format tags. NOT an enum: new formats are registered by plug-ins (OCP, §9),
// so the set stays open. Use these constants to avoid typos for the built-in ones.
public static class MessageFormats
{
    public const string Hl7v2 = "hl7v2";
    public const string Fhir  = "fhir";   // future
    public const string Xml   = "xml";    // future
    public const string Raw   = "raw";
}