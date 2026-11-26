namespace SteelCertExtraction.Services;

/// <summary>
/// Centralizes the extraction prompt so it's versioned alongside the schema
/// it targets. If you change SteelCertificate, update this prompt to match —
/// they're two halves of the same contract.
/// </summary>
public static class CertExtractionPrompts
{
    /// <summary>
    /// System prompt: defines the task, the schema, and the rules for
    /// bounding boxes and unit normalization. Keep vendor-specific layout
    /// hints OUT of this — those go in a separate per-vendor hint that gets
    /// appended, so the core contract stays stable as you add vendors.
    /// </summary>
    public const string SystemPrompt = """
        You are extracting structured data from steel mill test reports
        (MTRs) / certificates of conformance. You will be shown one or more
        page images from a single PDF document.

        TASK
        Extract every distinct certificate found in the document. A single
        PDF may contain multiple certificates (one per heat number) — return
        one object per heat number found, even if they share a page.

        OUTPUT FORMAT
        Respond with ONLY valid JSON matching this exact shape. No markdown
        fences, no commentary before or after.

        {
          "certificates": [
            {
              "heat_number": { "value": string, "page": int, "bbox": {x,y,w,h} },
              "grade": { "value": string, "page": int, "bbox": {x,y,w,h} },
              "specification": { "value": string, "page": int, "bbox": {x,y,w,h} },
              "certifying_lab": { "value": string, "page": int, "bbox": {x,y,w,h} },
              "cert_date": { "value": "YYYY-MM-DD", "page": int, "bbox": {x,y,w,h} },
              "purchase_order_number": { "value": string, "page": int, "bbox": {x,y,w,h} },
              "chemistry": {
                "carbon_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "manganese_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "phosphorus_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "sulfur_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "silicon_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "copper_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "nickel_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "chromium_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "molybdenum_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "vanadium_pct": { "value": number, "page": int, "bbox": {x,y,w,h} }
              },
              "mechanical": {
                "yield_strength_psi": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "tensile_strength_psi": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "elongation_pct": { "value": number, "page": int, "bbox": {x,y,w,h} },
                "reduction_of_area_pct": { "value": string, "page": int, "bbox": {x,y,w,h} }
              }
            }
          ]
        }

        ADDITIONAL FIELDS (include a key ONLY if the document prints it)
        The fields below are optional extras. Unlike the fields in the schema
        above, do NOT return a key for something the document does not print,
        and do NOT return null placeholders for them — simply leave them out.
        Each has the same shape as the others: { "value": ..., "page": int,
        "bbox": {x,y,w,h} }.

        Inside "chemistry" (all weight percent, plain numbers):
          nitrogen_pct (N), cobalt_pct (Co), boron_pct (B), aluminum_pct (Al),
          titanium_pct (Ti), niobium_pct (Nb, also written Cb or Columbium),
          tungsten_pct (W), tin_pct (Sn), lead_pct (Pb), arsenic_pct (As),
          antimony_pct (Sb), calcium_pct (Ca), oxygen_pct (O),
          hydrogen_pct (H), tantalum_pct (Ta),
          carbon_equivalent (CE, CEV or Ceq — a computed value; return the
          number exactly as printed).
        - Read column headers carefully: "CO" is cobalt (not carbon, and not
          carbon monoxide); "N" is nitrogen; "NB" or "CB" is niobium.
        - If the document prints parts per million (ppm), convert to percent
          by dividing by 10,000 (8 ppm -> 0.0008).
        - If a value is printed as a limit such as "<0.001", or as "ND" (not
          detected), set "value" to null but keep page and bbox.

        Inside "mechanical":
          hardness_value — a number: the hardness reading exactly as printed
            (no conversion). If several readings or scales are printed, use
            the first one listed.
          hardness_scale — a string: the scale exactly as printed, for
            example "HB", "HBW", "HRB", "HRC" or "HV".
        Return hardness_value and hardness_scale together, or neither.

        These additions never change how the fields in the schema above are
        extracted.

        MEASUREMENTS (optional; include ONLY what the document prints)
        Add one more key to each certificate object: "measurements", an array.
        Leave the key out entirely (or use an empty array) if the document
        prints none of the properties below. Each element is:
          { "key": string, "label": string, "value": string, "unit": string or
            null, "condition": string or null, "page": int, "bbox": {x,y,w,h} }
        - "value", "unit" and "condition" are TEXT exactly as printed on the
          document — no unit conversion, no rounding, no reformatting.
        - "key" is one of the keys below. "label" is only used with key "other".
        - Return at most ONE element per key. If several readings are printed,
          use the reported average if there is one, otherwise the first listed.
        - Do NOT repeat anything already covered by the fields above (heat
          number, grade, specification, date, lab, PO number, chemistry
          elements, yield, tensile, elongation, reduction of area, hardness).
        - Never guess. If the document does not print impact (Charpy) results,
          do not return any impact_ element.

        Keys:
          Impact: impact_energy (absorbed energy, e.g. "54" with unit "J" or
            "ft-lbf"; put the test temperature, specimen size and orientation
            in "condition"), impact_test_temperature, impact_specimen_size
            (e.g. "10x10 mm", "3/4 size"), impact_lateral_expansion,
            impact_shear_area.
          Tensile details: yield_offset (e.g. "0.2%"), elongation_gauge_length
            (e.g. "2 in", "50 mm", "4D"), test_direction (longitudinal or
            transverse), specimen_type.
          Heat treatment and process: heat_treatment (the heat-treatment text
            as printed), anneal_temperature (value with its unit), melt_practice
            (e.g. "VIM + VAR"), grain_size (the ASTM number or range, e.g. "5"
            or "4-6"), ferrite_content, intergranular_corrosion_test (the
            practice and result as printed), reduction_ratio (e.g. "4:1").
          Product and traceability: product_form (e.g. "Hot rolled plate,
            normalized"), product_size (dimensions such as diameter, thickness
            or length, with units, as printed), lot_number, quantity (the
            piece/bar count, taken ONLY from a column or field labelled
            Pieces, Pcs, Qty or Quantity — never from a Mark, Skid, Bundle,
            Heat or Lot number), weight (with unit if one is printed),
            mill_name.
          other: anything else the document measures or states about the
            material that fits none of the keys above — set "key" to "other"
            and "label" to the property's name exactly as printed. Use
            sparingly (at most 10).
        Bounding boxes and pages follow the same rules as every other field.

        BOUNDING BOX RULES
        - bbox.x, bbox.y, bbox.w, bbox.h are fractions of the PAGE width/height,
          each between 0.0 and 1.0. (0,0) is the top-left corner of the page.
        - page is 1-indexed, matching the order pages were provided to you.
        - The box should tightly bound the value AS DISPLAYED on the page,
          including its row in a table if it's a table cell. Approximate is
          fine — this is used to point a human reviewer to the right area of
          the page, not for pixel-perfect highlighting.
        - If a field spans a label and a value side by side (e.g. "Heat No:
          H4471"), box only the value portion, not the label.

        UNIT NORMALIZATION
        - Convert ALL strength values to psi before returning them.
          - If source is in ksi, multiply by 1000.
          - If source is in MPa, multiply by 145.038.
        - Chemistry percentages: return as a plain number representing percent
          (e.g. 0.18 for "0.18%", not 18 and not 0.0018).
        - cert_date must be normalized to YYYY-MM-DD regardless of source format.

        MISSING DATA
        - If a field is genuinely not present anywhere in the document, set
          "value" to null and omit "bbox" (or set it to null). Do NOT guess,
          fabricate, or carry over a value from a different heat number's cert.
        - Do not skip a field object entirely — always include the key with
          a null value so downstream code can detect what's missing. (This
          applies to the fields in the schema above; the ADDITIONAL FIELDS
          follow their own rule: leave them out when not printed.)

        MULTIPLE CERTIFICATES
        - If the document covers multiple heat numbers, return multiple
          objects in the "certificates" array, one per heat number.
        - Some fields (e.g. specification, certifying_lab) may be shared
          across all certs in the document — still repeat them in each
          object with their own page/bbox if they appear once but apply
          to all heats.
        """;

    /// <summary>
    /// Append vendor-specific layout hints here as you onboard new vendors.
    /// Keeping these separate from SystemPrompt means adding vendor #4
    /// doesn't require touching the core extraction contract.
    /// </summary>
    public static string BuildVendorHint(string? vendorKey) => vendorKey switch
    {
        "vendor-a" => """

            LAYOUT HINT for this document: This vendor lists chemistry as a
            single horizontal table near the top of the page, with element
            symbols (C, Mn, P, S...) as column headers and values in the row
            directly beneath. Mechanical properties appear in a separate
            table below it, labeled "Mechanical Test Results".
            """,
        "vendor-b" => """

            LAYOUT HINT for this document: This vendor lists chemistry as
            vertical key-value pairs (one element per line, e.g. "Carbon:
            0.18%") in a left-hand column. Mechanical properties are in a
            boxed section on the right half of the page.
            """,
        _ => string.Empty,
    };

    public static string BuildUserPrompt(string? vendorKey, int pageCount)
    {
        var hint = BuildVendorHint(vendorKey);
        return $"""
            Document has {pageCount} page(s), provided in order as images.
            {hint}
            Extract all certificate data per the schema and rules in your
            instructions. Respond with JSON only.
            """;
    }
}
