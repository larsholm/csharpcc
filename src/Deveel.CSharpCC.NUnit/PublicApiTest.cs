#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deveel.CSharpCC.Parser;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
public class PublicApiTest {
    [Test]
    public void LibraryRetainsItsPublicAndProtectedContracts() {
        var lines = new List<string>();
        foreach (var type in typeof(Options).Assembly.GetExportedTypes()) {
            lines.Add($"{type.FullName}: {type.Attributes} : {type.BaseType}; interfaces: {string.Join(",", type.GetInterfaces().Select(t => t.ToString()).Order(StringComparer.Ordinal))}");
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                string? signature = member switch {
                    MethodBase method when Visible(method) => $"{method.Attributes} {method} ({string.Join(",", method.GetParameters().Select(p => $"{p.Name} {p.Attributes} {p.DefaultValue}"))})",
                    FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly =>
                        $"{field.Attributes} {field}" + (field.IsLiteral ? $" = {field.GetRawConstantValue()}" : ""),
                    PropertyInfo property when property.GetAccessors(true).Any(Visible) => $"{property.Attributes} {property}",
                    EventInfo @event when Visible(@event.GetAddMethod(true)) => $"{@event.Attributes} {@event}",
                    _ => null
                };
                if (signature != null) lines.Add($"{type.FullName}.{member.MemberType}: {signature}");
            }
        }
        string[] expected = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, "Snapshots", "PublicApi.txt"));
        Assert.That(lines.Order(StringComparer.Ordinal), Is.EqualTo(expected));
    }

    private static bool Visible(MethodBase? method) => method != null &&
        (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);
}
