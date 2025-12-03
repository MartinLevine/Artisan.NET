using Artisan.Application;
using Artisan.Attributes;

namespace api_gateway
{
    [ArtisanApplication]
    public class Application
    {
        public static void Main(string[] args)
        {
            ArtisanApplication.Run(args);
        }
    }
}