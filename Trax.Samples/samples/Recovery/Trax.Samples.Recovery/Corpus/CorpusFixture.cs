namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// The topic map's corpus: 28 made-up papers in three fields, shaped like works from a scholarly
/// index. Canned so the demo needs no network. Each field holds two topics whose papers cite works
/// in common, and two pairs of papers from different fields read alike but cite nothing in common:
/// the hidden twins the map is meant to find.
/// </summary>
public static class CorpusFixture
{
    public const string Hydrology = "Hydrology";
    public const string SoilScience = "Soil science";
    public const string UrbanEcology = "Urban ecology";

    /// <summary>The fields the corpus covers.</summary>
    public static readonly IReadOnlyList<string> Fields = [Hydrology, SoilScience, UrbanEcology];

    public static readonly IReadOnlyList<Work> Works =
    [
        // ── Hydrology: groundwater recharge ──────────────────────────────────
        W(
            "W30001",
            2017,
            Hydrology,
            "Recharge pulses in a fractured chalk aquifer",
            "Groundwater recharge through fractured chalk arrives in pulses after winter storms. "
                + "Borehole levels and tracer arrivals locate the fractures that carry most recharge "
                + "to the aquifer.",
            ["Ines Varga", "Tomas Ruel"],
            ["R1001", "R1002", "R1003", "R1004"],
            ["Groundwater recharge", "Aquifer", "Tracer"]
        ),
        W(
            "W30002",
            2019,
            Hydrology,
            "Tracer estimates of recharge beneath irrigated valleys",
            "Chloride and isotope tracers estimate groundwater recharge beneath irrigated valley "
                + "floors. Recharge from irrigation return flow exceeds rainfall recharge in dry "
                + "years, raising aquifer levels.",
            ["Tomas Ruel", "Mei Okafor"],
            ["R1001", "R1002", "R1005"],
            ["Groundwater recharge", "Tracer", "Irrigation"]
        ),
        W(
            "W30003",
            2021,
            Hydrology,
            "Seasonal recharge windows in a sandstone aquifer",
            "Daily borehole records show recharge reaching the sandstone aquifer in a short window "
                + "each spring. Groundwater recharge in that window sets aquifer storage for the "
                + "whole year.",
            ["Ines Varga"],
            ["R1001", "R1003", "R1006"],
            ["Groundwater recharge", "Aquifer"]
        ),
        W(
            "W30004",
            2024,
            Hydrology,
            "Modelling recharge with soil moisture probes",
            "Soil moisture probes feed a model of groundwater recharge across a chalk catchment. "
                + "The model reproduces recharge pulses and borehole levels better than rainfall "
                + "alone.",
            ["Mei Okafor", "Ines Varga"],
            ["R1001", "R1002", "R1007"],
            ["Groundwater recharge", "Soil moisture", "Hydrological model"]
        ),
        // ── Hydrology: flood forecasting ─────────────────────────────────────
        W(
            "W30005",
            2016,
            Hydrology,
            "Ensemble flood forecasts for a mountain river",
            "Ensemble forecasts of river discharge give flood warnings two days ahead on a mountain "
                + "river. Forecast spread tracks snowmelt timing and the skill of rainfall "
                + "predictions.",
            ["Dara Lindqvist", "Pavel Novak"],
            ["R1011", "R1012", "R1013"],
            ["Flood forecasting", "Ensemble forecasting", "Discharge"]
        ),
        W(
            "W30006",
            2018,
            Hydrology,
            "Radar rainfall and flash flood warnings",
            "Weather radar rainfall drives a flash flood forecast for small catchments. Warnings "
                + "issued from radar nowcasts arrive an hour before peak river discharge.",
            ["Pavel Novak", "Nadia Haddad"],
            ["R1011", "R1014"],
            ["Flood forecasting", "Weather radar", "Flash flood"]
        ),
        W(
            "W30007",
            2020,
            Hydrology,
            "Learning flood peaks from upstream gauges",
            "A forecast model trained on upstream gauges predicts flood peaks and river discharge "
                + "downstream. Forecast errors grow when upstream gauges fail during the flood.",
            ["Nadia Haddad"],
            ["R1011", "R1012", "R1015"],
            ["Flood forecasting", "Discharge", "Stream gauge"]
        ),
        W(
            "W30008",
            2023,
            Hydrology,
            "Verifying flood warnings against reported damage",
            "Flood warnings from an operational forecast system are checked against reported "
                + "damage. Ensemble forecasts with wide spread missed fewer floods but raised more "
                + "false warnings.",
            ["Dara Lindqvist"],
            ["R1011", "R1013", "R1016"],
            ["Flood forecasting", "Ensemble forecasting", "Verification"]
        ),
        // ── Hydrology: the first hidden twin ─────────────────────────────────
        W(
            "W30009",
            2022,
            Hydrology,
            "Stormwater runoff from paved city catchments",
            "Paved roofs and streets turn city rainfall into fast stormwater runoff. Retention "
                + "storage on roofs and in tanks delays the runoff peak and lowers peak stormwater "
                + "flow from each rainfall event.",
            ["Oskar Berg"],
            ["R1021", "R1022", "R1023"],
            ["Stormwater", "Urban runoff", "Retention"]
        ),
        // ── Hydrology: the second hidden twin ────────────────────────────────
        W(
            "W30010",
            2021,
            Hydrology,
            "Nitrate in shallow groundwater under farmland",
            "Nitrate from fertilizer reaches shallow groundwater under farmland within a few "
                + "seasons. Nitrate concentrations rise after heavy fertilizer use on cropland and "
                + "fall slowly once leaching stops.",
            ["Yara Sato"],
            ["R1024", "R1025"],
            ["Nitrate", "Groundwater quality", "Agriculture"]
        ),
        // ── Soil science: soil carbon ────────────────────────────────────────
        W(
            "W30011",
            2017,
            SoilScience,
            "Cover crops and soil organic carbon",
            "Cover crops grown between harvests raise soil organic carbon in the topsoil. Carbon "
                + "gains are largest in clay soils and level off after a decade of cover cropping.",
            ["Lena Brandt", "Kofi Mensah"],
            ["R1031", "R1032", "R1033"],
            ["Soil organic carbon", "Cover crop"]
        ),
        W(
            "W30012",
            2019,
            SoilScience,
            "Deep soil carbon under perennial grasses",
            "Perennial grass roots store soil organic carbon well below the plough layer. Deep "
                + "carbon stocks under grasses exceed those under annual crops on the same soils.",
            ["Kofi Mensah"],
            ["R1031", "R1034"],
            ["Soil organic carbon", "Perennial grass", "Root"]
        ),
        W(
            "W30013",
            2022,
            SoilScience,
            "Measuring soil carbon change with spectroscopy",
            "Infrared spectroscopy measures soil organic carbon in field samples at low cost. "
                + "Repeated surveys detect carbon change in topsoil after five years of cover crops.",
            ["Ana Ruiz", "Lena Brandt"],
            ["R1031", "R1032", "R1035"],
            ["Soil organic carbon", "Spectroscopy"]
        ),
        W(
            "W30014",
            2025,
            SoilScience,
            "Tillage and the loss of soil carbon",
            "Ploughing exposes soil organic carbon to microbes and speeds its loss. Fields kept "
                + "under reduced tillage held more topsoil carbon than ploughed fields nearby.",
            ["Ana Ruiz"],
            ["R1031", "R1033", "R1036"],
            ["Soil organic carbon", "Tillage"]
        ),
        // ── Soil science: erosion ────────────────────────────────────────────
        W(
            "W30015",
            2016,
            SoilScience,
            "Gully erosion on loess hillslopes",
            "Gully erosion on loess hillslopes removes topsoil after intense summer storms. Mapping "
                + "gully heads from drone images shows erosion concentrated along tracks and field "
                + "edges.",
            ["Henrik Dahl", "Sara Quint"],
            ["R1041", "R1042", "R1043"],
            ["Soil erosion", "Gully", "Loess"]
        ),
        W(
            "W30016",
            2018,
            SoilScience,
            "Sediment fences and hillslope erosion",
            "Sediment fences on hillslopes trap eroded soil before it reaches streams. Erosion "
                + "plots show fences cut sediment loss by half on slopes under bare fallow.",
            ["Sara Quint"],
            ["R1041", "R1044"],
            ["Soil erosion", "Sediment", "Conservation"]
        ),
        W(
            "W30017",
            2020,
            SoilScience,
            "Wind erosion of dry cropland soils",
            "Wind erosion strips fine soil from dry cropland in spring before crops cover the "
                + "ground. Erosion rates measured by dust traps fall where stubble is left standing.",
            ["Henrik Dahl"],
            ["R1041", "R1042", "R1045"],
            ["Soil erosion", "Wind erosion"]
        ),
        W(
            "W30018",
            2023,
            SoilScience,
            "Rainfall intensity and erosion from bare soil",
            "Rainfall simulators apply storms of rising intensity to bare soil plots. Erosion and "
                + "sediment loss climb sharply once rainfall intensity passes a threshold that "
                + "seals the soil surface.",
            ["Sara Quint", "Henrik Dahl"],
            ["R1041", "R1043", "R1046"],
            ["Soil erosion", "Rainfall simulation", "Sediment"]
        ),
        // ── Soil science: the second hidden twin ─────────────────────────────
        W(
            "W30019",
            2022,
            SoilScience,
            "Nitrate leaching below fertilized cropland",
            "Nitrate leaching below fertilized cropland carries nitrate past the roots toward "
                + "shallow groundwater. Leaching peaks in wet seasons after fertilizer is spread and "
                + "falls once cropland is kept under cover.",
            ["Iris Hale"],
            ["R1047", "R1048"],
            ["Nitrate", "Leaching", "Fertilizer"]
        ),
        // ── Urban ecology: heat islands ──────────────────────────────────────
        W(
            "W30020",
            2017,
            UrbanEcology,
            "Mapping the urban heat island with car sensors",
            "Temperature sensors on cars map the urban heat island street by street. Night "
                + "temperatures in dense districts stay several degrees warmer than in parks.",
            ["Noor Aziz", "Felix Moreau"],
            ["R1051", "R1052", "R1053"],
            ["Urban heat island", "Air temperature"]
        ),
        W(
            "W30021",
            2019,
            UrbanEcology,
            "Parks as cool islands in a warming city",
            "City parks cool surrounding streets on summer nights. The cooling reaches further from "
                + "large parks with trees than from lawns, softening the urban heat island.",
            ["Felix Moreau"],
            ["R1051", "R1054"],
            ["Urban heat island", "Urban park", "Cooling"]
        ),
        W(
            "W30022",
            2021,
            UrbanEcology,
            "Heat island intensity and pavement colour",
            "Light coloured pavements reflect sunlight and lower surface temperature in summer. "
                + "Districts repaved in light colours show a weaker urban heat island at midday.",
            ["Noor Aziz", "Lucia Ferro"],
            ["R1051", "R1052", "R1055"],
            ["Urban heat island", "Pavement", "Albedo"]
        ),
        W(
            "W30023",
            2024,
            UrbanEcology,
            "Heat exposure of residents in warm districts",
            "Indoor temperature loggers show residents of warm districts exposed to more hot nights. "
                + "Heat exposure follows the urban heat island and the age of the housing.",
            ["Lucia Ferro"],
            ["R1051", "R1053", "R1056"],
            ["Urban heat island", "Heat exposure"]
        ),
        // ── Urban ecology: street trees and pollinators ──────────────────────
        W(
            "W30024",
            2016,
            UrbanEcology,
            "Street tree diversity across city districts",
            "A census of street trees counts species in every district. Tree diversity is lowest "
                + "in newer districts planted with a few fast growing species.",
            ["Mateo Silva", "Greta Holm"],
            ["R1061", "R1062", "R1063"],
            ["Street tree", "Biodiversity"]
        ),
        W(
            "W30025",
            2018,
            UrbanEcology,
            "Bees foraging on flowering street trees",
            "Wild bees forage on flowering street trees in spring when little else blooms. Bee "
                + "counts are highest along streets planted with lime and cherry trees.",
            ["Greta Holm"],
            ["R1061", "R1064"],
            ["Pollinator", "Street tree", "Wild bee"]
        ),
        W(
            "W30026",
            2020,
            UrbanEcology,
            "Pollinators in road verges and small gardens",
            "Road verges and small gardens hold pollinators between larger parks. Pollinator "
                + "visits rise where verges are mown less often and flowering plants are left.",
            ["Mateo Silva"],
            ["R1061", "R1062", "R1065"],
            ["Pollinator", "Road verge", "Biodiversity"]
        ),
        W(
            "W30027",
            2023,
            UrbanEcology,
            "Street tree survival in paved planting pits",
            "Young street trees in small paved pits die more often in dry summers. Larger pits "
                + "and shared root trenches raise tree survival in paved streets.",
            ["Greta Holm", "Mateo Silva"],
            ["R1061", "R1063", "R1066"],
            ["Street tree", "Tree survival"]
        ),
        // ── Urban ecology: the first hidden twin ─────────────────────────────
        W(
            "W30028",
            2024,
            UrbanEcology,
            "Green roofs hold back city stormwater",
            "Green roofs store rainfall in their soil and plants and release it slowly. Roof "
                + "retention delays the stormwater runoff peak and lowers peak flow from city "
                + "rainfall events.",
            ["Eva Lund"],
            ["R1067", "R1068"],
            ["Green roof", "Stormwater", "Retention"]
        ),
    ];

    private static Work W(
        string id,
        int year,
        string field,
        string title,
        string @abstract,
        List<string> authors,
        List<string> references,
        List<string> concepts
    ) =>
        new()
        {
            Id = id,
            Year = year,
            Field = field,
            Title = title,
            Abstract = @abstract,
            Authors = authors,
            References = references,
            Concepts = concepts,
        };
}
