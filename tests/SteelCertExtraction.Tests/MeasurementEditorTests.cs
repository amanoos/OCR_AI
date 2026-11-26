using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class MeasurementEditorTests
{
    private static ExtractedMeasurement Extracted(string key, string label, string category, string value, string? unit = null, bool confirmed = false)
    {
        var v = new MeasurementValue(value, unit);
        return new ExtractedMeasurement
        {
            Key = key,
            Label = label,
            Category = category,
            Field = new ExtractedField<MeasurementValue> { Value = v, OriginalValue = v, PageNumber = 1, Confirmed = confirmed },
        };
    }

    private static SteelCertificate Cert(params ExtractedMeasurement[] measurements)
    {
        var cert = new SteelCertificate();
        cert.Measurements.AddRange(measurements);
        return cert;
    }

    // --- available kinds ---

    [Fact]
    public void All_22_kinds_are_available_on_a_certificate_with_none()
    {
        Assert.Equal(22, MeasurementEditor.AvailableKinds(Cert()).Count);
    }

    [Fact]
    public void Kinds_already_listed_with_a_value_are_not_offered_again()
    {
        var cert = Cert(Extracted("grain_size", "Grain size", "HeatTreatment", "6-8"));

        var keys = MeasurementEditor.AvailableKinds(cert).Select(k => k.Key).ToList();

        Assert.DoesNotContain("grain_size", keys);
        Assert.Equal(21, keys.Count);
    }

    [Fact]
    public void A_removed_kind_is_offered_again()
    {
        var m = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        var cert = Cert(m);
        MeasurementEditor.Remove(cert, m);

        Assert.Contains(MeasurementEditor.AvailableKinds(cert), k => k.Key == "grain_size");
    }

    // --- add ---

    [Fact]
    public void Adding_a_catalog_kind_creates_an_unvalued_measurement_with_the_catalog_label_and_category()
    {
        var cert = Cert();

        var (m, error) = MeasurementEditor.Add(cert, "impact_energy", null);

        Assert.Null(error);
        Assert.Same(m, Assert.Single(cert.Measurements));
        Assert.Equal("impact_energy", m!.Key);
        Assert.Equal("Impact energy", m.Label);
        Assert.Equal("Impact", m.Category);
        Assert.Null(m.Field.Value);
        Assert.Null(m.Field.OriginalValue);   // added by a reviewer: no original
        Assert.Equal(0, m.Field.PageNumber);
        Assert.Null(m.Field.BoundingBox);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Adding_without_choosing_a_type_is_an_error(string? key)
    {
        var (m, error) = MeasurementEditor.Add(Cert(), key, null);

        Assert.Null(m);
        Assert.Equal("Choose a measurement type.", error);
    }

    [Fact]
    public void Adding_an_unknown_type_is_an_error()
    {
        var (m, error) = MeasurementEditor.Add(Cert(), "made_up", null);

        Assert.Null(m);
        Assert.Equal("Unknown measurement type.", error);
    }

    [Fact]
    public void Adding_a_kind_that_is_already_listed_is_an_error_and_adds_nothing()
    {
        var cert = Cert(Extracted("grain_size", "Grain size", "HeatTreatment", "6-8"));

        var (m, error) = MeasurementEditor.Add(cert, "grain_size", null);

        Assert.Null(m);
        Assert.Equal("Grain size is already listed.", error);
        Assert.Single(cert.Measurements);
    }

    [Fact]
    public void Re_adding_a_removed_kind_returns_the_same_measurement_with_its_original_intact()
    {
        var original = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        var cert = Cert(original);
        MeasurementEditor.Remove(cert, original);

        var (m, error) = MeasurementEditor.Add(cert, "grain_size", null);

        Assert.Null(error);
        Assert.Same(original, m);
        Assert.Equal(new MeasurementValue("6-8"), m!.Field.OriginalValue);
        Assert.Single(cert.Measurements);
    }

    [Fact]
    public void Adding_a_custom_measurement_uses_other_with_the_typed_label()
    {
        var cert = Cert();

        var (m, error) = MeasurementEditor.Add(cert, "other", "  Delta ferrite ");

        Assert.Null(error);
        Assert.Equal("other", m!.Key);
        Assert.Equal("Delta ferrite", m.Label);
        Assert.Equal("Other", m.Category);
        Assert.Equal("Measurement:other:Delta ferrite", m.EvidenceName);
    }

    [Theory]
    [InlineData(null, "Label is required.")]
    [InlineData("  ", "Label is required.")]
    public void A_custom_measurement_needs_a_label(string? label, string expected)
    {
        var (m, error) = MeasurementEditor.Add(Cert(), "other", label);

        Assert.Null(m);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void A_custom_label_cannot_exceed_100_characters()
    {
        var (m, error) = MeasurementEditor.Add(Cert(), "other", new string('L', 101));

        Assert.Null(m);
        Assert.Equal("Label must be at most 100 characters.", error);
    }

    [Fact]
    public void A_custom_label_that_is_already_listed_is_rejected_ignoring_case()
    {
        var cert = Cert(Extracted("other", "Delta ferrite", "Other", "5"));

        var (m, error) = MeasurementEditor.Add(cert, "other", "DELTA FERRITE");

        Assert.Null(m);
        Assert.Equal("\"DELTA FERRITE\" is already listed.", error);
    }

    [Fact]
    public void At_most_ten_custom_measurements_can_be_added()
    {
        var cert = Cert();
        for (var i = 1; i <= 10; i++)
        {
            cert.Measurements.Add(Extracted("other", $"Thing {i}", "Other", i.ToString()));
        }

        var (m, error) = MeasurementEditor.Add(cert, "other", "One too many");

        Assert.Null(m);
        Assert.Equal("At most 10 custom measurements.", error);
        Assert.False(MeasurementEditor.CanAddOther(cert));
    }

    [Fact]
    public void Removing_a_custom_measurement_frees_a_slot()
    {
        var cert = Cert();
        for (var i = 1; i <= 10; i++) cert.Measurements.Add(Extracted("other", $"Thing {i}", "Other", i.ToString()));
        MeasurementEditor.Remove(cert, cert.Measurements[0]);

        Assert.True(MeasurementEditor.CanAddOther(cert));
    }

    // --- apply ---

    [Fact]
    public void Applying_a_valid_value_sets_it_trimmed_and_marks_the_measurement_edited()
    {
        var m = Extracted("impact_energy", "Impact energy", "Impact", "54", "J", confirmed: true);

        var outcome = MeasurementEditor.Apply(m, " 60 ", "J", " -20 C ");

        Assert.True(outcome.IsValid);
        Assert.Equal(new MeasurementValue("60", "J", "-20 C"), m.Field.Value);
        Assert.True(m.Field.IsEdited);
        Assert.False(m.Field.Confirmed);   // an edit must be confirmed again
    }

    [Fact]
    public void Invalid_input_changes_nothing_and_reports_per_part_errors()
    {
        var m = Extracted("impact_energy", "Impact energy", "Impact", "54", "J", confirmed: true);

        var outcome = MeasurementEditor.Apply(m, "", new string('u', 21), null);

        Assert.False(outcome.IsValid);
        Assert.Equal("Value is required.", outcome.ValueError);
        Assert.Equal("Unit must be at most 20 characters.", outcome.UnitError);
        Assert.Equal(new MeasurementValue("54", "J"), m.Field.Value);
        Assert.True(m.Field.Confirmed);
    }

    [Fact]
    public void Typing_the_same_value_keeps_the_confirmation()
    {
        var m = Extracted("impact_energy", "Impact energy", "Impact", "54", "J", confirmed: true);

        MeasurementEditor.Apply(m, "54", "J", "");

        Assert.True(m.Field.Confirmed);
        Assert.False(m.Field.IsEdited);
    }

    [Fact]
    public void Applying_a_value_to_an_added_measurement_makes_it_edited_with_no_original()
    {
        var cert = Cert();
        var (m, _) = MeasurementEditor.Add(cert, "lot_number", null);

        MeasurementEditor.Apply(m!, "L-77", null, null);

        Assert.Equal(new MeasurementValue("L-77"), m!.Field.Value);
        Assert.Null(m.Field.OriginalValue);
        Assert.True(m.Field.IsEdited);
        Assert.False(m.Field.Confirmed);
    }

    // --- remove / revert / discard ---

    [Fact]
    public void Removing_an_extracted_measurement_clears_it_but_keeps_it_listed_for_revert()
    {
        var m = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8", confirmed: true);
        var cert = Cert(m);

        MeasurementEditor.Remove(cert, m);

        Assert.Same(m, Assert.Single(cert.Measurements));
        Assert.Null(m.Field.Value);
        Assert.Equal(new MeasurementValue("6-8"), m.Field.OriginalValue);
        Assert.True(m.Field.IsEdited);
        Assert.False(m.Field.Confirmed);
        Assert.False(MeasurementEditor.HasValue(m));
    }

    [Fact]
    public void Removing_a_measurement_the_reviewer_added_drops_it_entirely()
    {
        var cert = Cert();
        var (m, _) = MeasurementEditor.Add(cert, "lot_number", null);
        MeasurementEditor.Apply(m!, "L-77", null, null);

        MeasurementEditor.Remove(cert, m!);

        Assert.Empty(cert.Measurements);
    }

    [Fact]
    public void Revert_restores_the_original_and_unconfirms()
    {
        var m = Extracted("impact_energy", "Impact energy", "Impact", "54", "J");
        MeasurementEditor.Apply(m, "60", "J", null);
        m.Field.Confirmed = true;

        MeasurementEditor.Revert(m);

        Assert.Equal(new MeasurementValue("54", "J"), m.Field.Value);
        Assert.False(m.Field.IsEdited);
        Assert.False(m.Field.Confirmed);
    }

    [Fact]
    public void Revert_brings_back_a_removed_measurement()
    {
        var m = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        var cert = Cert(m);
        MeasurementEditor.Remove(cert, m);

        MeasurementEditor.Revert(m);

        Assert.True(MeasurementEditor.HasValue(m));
        Assert.False(m.Field.IsEdited);
    }

    [Fact]
    public void Revert_does_nothing_for_an_added_measurement()
    {
        var cert = Cert();
        var (m, _) = MeasurementEditor.Add(cert, "lot_number", null);
        MeasurementEditor.Apply(m!, "L-77", null, null);

        MeasurementEditor.Revert(m!);

        Assert.Equal(new MeasurementValue("L-77"), m!.Field.Value);
    }

    [Fact]
    public void Discarding_drops_a_cancelled_add_but_keeps_a_removed_extraction()
    {
        var cert = Cert();
        var (cancelled, _) = MeasurementEditor.Add(cert, "lot_number", null);
        var removed = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        cert.Measurements.Add(removed);
        MeasurementEditor.Remove(cert, removed);

        MeasurementEditor.DiscardIfEmpty(cert, cancelled!);
        MeasurementEditor.DiscardIfEmpty(cert, removed);

        Assert.DoesNotContain(cancelled, cert.Measurements);
        Assert.Contains(removed, cert.Measurements);
    }

    [Fact]
    public void Discarding_never_drops_a_measurement_that_has_a_value()
    {
        var m = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        var cert = Cert(m);

        MeasurementEditor.DiscardIfEmpty(cert, m);

        Assert.Single(cert.Measurements);
    }

    // --- what is listed, and in what order ---

    [Fact]
    public void Listing_is_in_category_order_then_the_models_order()
    {
        var cert = Cert(
            Extracted("product_form", "Product form", "Product", "Bar"),
            Extracted("other", "Delta ferrite", "Other", "5"),
            Extracted("grain_size", "Grain size", "HeatTreatment", "6-8"),
            Extracted("impact_energy", "Impact energy", "Impact", "54"),
            Extracted("weight", "Weight", "Product", "2,186"),
            Extracted("test_direction", "Test direction", "Tensile", "L"));

        var keys = MeasurementEditor.Ordered(cert).Select(m => m.Key).ToList();

        Assert.Equal(new[] { "impact_energy", "test_direction", "grain_size", "product_form", "weight", "other" }, keys);
    }

    [Fact]
    public void Listing_includes_removed_extractions_and_open_editors_but_not_empty_leftovers()
    {
        var removed = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        var cert = Cert(removed);
        MeasurementEditor.Remove(cert, removed);
        var (adding, _) = MeasurementEditor.Add(cert, "lot_number", null);
        var (leftover, _) = MeasurementEditor.Add(cert, "quantity", null);

        var withoutOpen = MeasurementEditor.Ordered(cert).ToList();
        var withOpen = MeasurementEditor.Ordered(cert, new[] { adding! }).ToList();

        Assert.Equal(new[] { removed }, withoutOpen);            // the removed one only
        Assert.Contains(adding, withOpen);                        // an add being edited is shown
        Assert.DoesNotContain(leftover, withOpen);                // an abandoned empty one is not
    }

    [Fact]
    public void Evidence_stays_consistent_through_edit_remove_and_re_add()
    {
        // Extract -> remove -> re-add with a new value: still ONE measurement, original preserved, marked edited.
        var m = Extracted("grain_size", "Grain size", "HeatTreatment", "6-8");
        var cert = Cert(m);
        MeasurementEditor.Remove(cert, m);

        var (again, _) = MeasurementEditor.Add(cert, "grain_size", null);
        MeasurementEditor.Apply(again!, "7", null, null);

        Assert.Same(m, again);
        Assert.Single(cert.Measurements);
        Assert.Equal(new MeasurementValue("6-8"), m.Field.OriginalValue);
        Assert.Equal(new MeasurementValue("7"), m.Field.Value);
        Assert.True(m.Field.IsEdited);
    }
}
