using NetTopologySuite.Geometries;

namespace SengokuProvider.Library.Models.Common
{
    public struct DiscordWebhookConfig
    {
        public static readonly string BaseWebhookUrl = $"https://discord.com/api/webhooks/1274518441151299728/Qbw_v9ODfHOleRXp86_YvvNohy9H4GnWD8BwDTHA1fWjwDimTOyPv9GqyBa1ZSbUUPSI";
        public static readonly long Tekken8ThreadId = 1273004220831891517;
        public static readonly long StreetFighter6ThreadId = 1273004373890568314;
        public static readonly long GuiltyGearStriveThreadId = 1273004304764108810;
    }
    public struct CommonConstants
    {
        public static readonly Dictionary<int, int> EnhancedPointDistribution = new Dictionary<int, int>
        {
            { 1, 150 },
            { 2, 100 },
            { 3, 80 },
            { 4, 70 },
            { 5, 60 },
            { 7, 50 },
            { 9, 40 },
            { 13, 30 },
            { 17, 20 },
            { 25, 10 },
            { 33, 10 }
        };
    }
    public struct BearerConstants
    {
        public static readonly string[] BearerArray = ["2a7b93add38208847a394e01bd3e4575", "2b0160ef106be1c250e4db077b743429", "10cf2895837a1e6e9ea826fd997180e0",
            "72730e024c5658ebc2b99710cb1119e4", "758d4c2a29fe920513d61550c54744b0", "85d0bf37972496fdea13addc7a2634b1"];
        public static readonly Queue<string> TokenQueue = new Queue<string>(BearerArray);
    }
    public struct QueryConstants
    {
        public const string DatePriority = @"
                            SELECT 
                                a.address, a.latitude, a.longitude, 
                                e.event_name, e.event_description, e.region, e.start_time, e.end_time, e.link_id, e.closing_registration_date, e.registration_open, e.url_slug, e.online_tournament,
                                SQRT(
                                    POW(a.longitude - @ReferenceLongitude, 2) + POW(a.latitude - @ReferenceLatitude, 2)
                                ) AS distance
                            FROM 
                                events e
                            JOIN 
                                addresses a ON e.address_id = a.id
                            WHERE
                                e.region = ANY(@RegionIds)
                                AND e.closing_registration_date >= CURRENT_DATE
                                AND e.start_time >= CURRENT_DATE
                            ORDER BY
                                e.closing_registration_date ASC,
                                distance ASC
                            LIMIT @PerPage;";

        public const string DistancePriority = @"
                            SELECT 
                                a.address, a.latitude, a.longitude, 
                                e.event_name, e.event_description, e.region, e.start_time, e.end_time, e.link_id, e.closing_registration_date, e.registration_open, e.url_slug, e.online_tournament,
                                SQRT(
                                    POW(a.longitude - @ReferenceLongitude, 2) + POW(a.latitude - @ReferenceLatitude, 2)
                                ) AS distance
                            FROM 
                                events e
                            JOIN 
                                addresses a ON e.address_id = a.id
                            WHERE
                                e.region = ANY(@RegionIds)
                                AND e.closing_registration_date >= CURRENT_DATE
                                AND e.start_time >= CURRENT_DATE
                            ORDER BY
                                distance ASC,
                                e.closing_registration_date ASC
                            LIMIT @PerPage;";
    }
    public struct GeoConstants
    {
        private static Polygon CreatePolygon(Coordinate[] coordinates)
        {
            // Ensure the polygon is closed by repeating the first coordinate at the end
            if (!coordinates[0].Equals2D(coordinates[coordinates.Length - 1]))
            {
                var closedCoordinates = new Coordinate[coordinates.Length + 1];
                Array.Copy(coordinates, closedCoordinates, coordinates.Length);
                closedCoordinates[closedCoordinates.Length - 1] = coordinates[0];
                coordinates = closedCoordinates;
            }

            var polygon = new Polygon(new LinearRing(coordinates));
            if (!polygon.IsValid)
            {
                throw new ArgumentException("Invalid polygon geometry");
            }

            return polygon;
        }

        public static readonly Dictionary<string, Polygon> Regions;

        static GeoConstants()
        {
            try
            {
                Regions = new Dictionary<string, Polygon>
            {
                {
                    "South East", CreatePolygon(new[]
                    {
                        new Coordinate(-89.5166016, 36.4566360),
                        new Coordinate(-91.2084961, 33.0270876),
                        new Coordinate(-94.2407227, 32.9902356),
                        new Coordinate(-93.8671875, 29.4587312),
                        new Coordinate(-88.8354492, 28.9408618),
                        new Coordinate(-88.7036133, 29.9739702),
                        new Coordinate(-83.9355469, 29.7071393),
                        new Coordinate(-81.2109375, 24.8465653),
                        new Coordinate(-79.9365234, 25.1850589),
                        new Coordinate(-79.7167969, 26.6867295),
                        new Coordinate(-81.2109375, 30.6757154),
                        new Coordinate(-75.8276367, 35.2097216),
                        new Coordinate(-75.7891846, 36.5361226),
                        new Coordinate(-88.0993652, 36.6772306),
                        new Coordinate(-89.4836426, 36.4743068),
                        new Coordinate(-89.5166016, 36.4566360)
                    })
                },
                {
                    "North East", CreatePolygon(new[]
                    {
                        new Coordinate(-80.2661133, 44.7467332),
                        new Coordinate(-80.5297852, 39.7747695),
                        new Coordinate(-75.7727051, 39.7114125),
                        new Coordinate(-75.7150269, 39.7979858),
                        new Coordinate(-75.6628418, 39.8169751),
                        new Coordinate(-75.6079102, 39.8338501),
                        new Coordinate(-75.4486084, 39.8169751),
                        new Coordinate(-75.4032898, 39.7958755),
                        new Coordinate(-74.9652100, 38.8268705),
                        new Coordinate(-71.2133789, 41.0917722),
                        new Coordinate(-70.1586914, 41.0793511),
                        new Coordinate(-69.6588135, 41.7180305),
                        new Coordinate(-71.1694336, 46.1798304),
                        new Coordinate(-75.5639648, 45.8287993),
                        new Coordinate(-80.3759766, 44.7467332),
                        new Coordinate(-80.2661133, 44.7467332)
                    })
                },
                {
                    "MVD", CreatePolygon(new[]
                    {
                        new Coordinate(-75.5090332, 36.5449494),
                        new Coordinate(-83.3642578, 36.4919735),
                        new Coordinate(-82.1337891, 37.3352244),
                        new Coordinate(-82.6171875, 38.2381801),
                        new Coordinate(-80.5957031, 39.7747695),
                        new Coordinate(-75.7617188, 39.6902806),
                        new Coordinate(-74.6850586, 38.4105583),
                        new Coordinate(-75.4760742, 36.5096362),
                        new Coordinate(-75.5090332, 36.5449494)
                    })
                },
                {
                    "MidWest", CreatePolygon(new[]
                    {
                        new Coordinate(-104.1064453, 48.9513665),
                        new Coordinate(-109.1162109, 40.8802948),
                        new Coordinate(-109.0283203, 31.2409854),
                        new Coordinate(-108.0175781, 31.7655374),
                        new Coordinate(-106.4355469, 31.5785354),
                        new Coordinate(-102.5244141, 29.7643774),
                        new Coordinate(-100.0195313, 28.1107488),
                        new Coordinate(-96.4599609, 28.1107488),
                        new Coordinate(-93.6914063, 29.4204603),
                        new Coordinate(-94.0429688, 33.4681080),
                        new Coordinate(-90.3515625, 34.8498750),
                        new Coordinate(-89.0332031, 36.9498918),
                        new Coordinate(-84.6386719, 38.8910328),
                        new Coordinate(-84.5947266, 41.5743613),
                        new Coordinate(-82.3974609, 42.1308213),
                        new Coordinate(-82.5732422, 45.2748864),
                        new Coordinate(-89.6044922, 48.0193242),
                        new Coordinate(-95.3173828, 49.0666684),
                        new Coordinate(-104.1064453, 48.9513665)
                    })
                },
                {
                    "PNW", CreatePolygon(new[]
                    {
                        new Coordinate(-126.4746094, 50.1205781),
                        new Coordinate(-125.4418945, 48.5311570),
                        new Coordinate(-124.3872070, 41.9513199),
                        new Coordinate(-115.2905273, 41.9513199),
                        new Coordinate(-114.6533203, 49.0234615),
                        new Coordinate(-113.0712891, 51.2619149),
                        new Coordinate(-125.3320313, 51.5497510),
                        new Coordinate(-126.5844727, 50.0641917),
                        new Coordinate(-126.4746094, 50.1205781)
                    })
                },
                {
                    "California", CreatePolygon(new[]
                    {
                        new Coordinate(-124.2114258, 41.9839943),
                        new Coordinate(-124.3872070, 40.1452893),
                        new Coordinate(-120.7177734, 34.2889919),
                        new Coordinate(-117.4877930, 32.5838493),
                        new Coordinate(-114.6533203, 32.6763728),
                        new Coordinate(-114.6752930, 35.0839556),
                        new Coordinate(-120.1025391, 39.0106475),
                        new Coordinate(-120.0366211, 41.9676592),
                        new Coordinate(-124.1015625, 41.9349765),
                        new Coordinate(-124.2114258, 41.9839943)
                    })
                },
                {
                    "West Coast", CreatePolygon(new[]
                    {
                        new Coordinate(-124.3872070, 41.9839943),
                        new Coordinate(-124.5410156, 40.1788733),
                        new Coordinate(-120.4321289, 33.7060627),
                        new Coordinate(-117.4438477, 32.5653332),
                        new Coordinate(-114.8071289, 32.6208702),
                        new Coordinate(-110.9179688, 31.2221970),
                        new Coordinate(-109.0502930, 31.2785509),
                        new Coordinate(-108.9624023, 40.8969058),
                        new Coordinate(-113.4008789, 41.9186289),
                        new Coordinate(-124.3432617, 41.9349765),
                        new Coordinate(-124.3872070, 41.9839943)
                    })
                }
            };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing GeoConstants: {ex.Message} - {ex.StackTrace}");
                throw;
            }
        }
    }
}
