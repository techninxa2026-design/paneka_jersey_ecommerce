using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jersey_ecommerce.Services
{
    public class ProfileImageService
    {
        private const long MaxFileSize = 2 * 1024 * 1024; // 2 MB
        private const string RelativeDirectory = "uploads/profiles";
        private readonly string _physicalDirectory;

        public ProfileImageService(IWebHostEnvironment environment)
        {
            string webRoot = environment.WebRootPath
                ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            _physicalDirectory = Path.Combine(webRoot, "uploads", "profiles");
        }

        public async Task<string> SaveAsync(IFormFile image, CancellationToken cancellationToken = default)
        {
            if (image.Length == 0 || image.Length > MaxFileSize)
                throw new InvalidDataException("Choose an image smaller than 2 MB.");

            string extension = Path.GetExtension(image.FileName).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
                throw new InvalidDataException("Only JPG, PNG and WEBP images are allowed.");

            // File extensions and ContentType can be spoofed; check the magic bytes too.
            await using var input = image.OpenReadStream();
            byte[] header = new byte[12];
            int count = 0;
            while (count < header.Length)
            {
                int read = await input.ReadAsync(
                    header.AsMemory(count, header.Length - count), cancellationToken);
                if (read == 0) break;
                count += read;
            }

            bool jpg = count >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            bool png = count >= 8 &&
                header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            bool webp = count >= 12 &&
                header.AsSpan(0, 4).SequenceEqual(new byte[] { 82, 73, 70, 70 }) &&
                header.AsSpan(8, 4).SequenceEqual(new byte[] { 87, 69, 66, 80 });

            bool valid = extension switch
            {
                ".jpg" or ".jpeg" => jpg,
                ".png" => png,
                ".webp" => webp,
                _ => false
            };

            if (!valid)
                throw new InvalidDataException("The selected file is not a valid image format.");

            Directory.CreateDirectory(_physicalDirectory);
            string fileName = $"{Guid.NewGuid():N}{extension}";
            string path = Path.Combine(_physicalDirectory, fileName);

            try
            {
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                await output.WriteAsync(header.AsMemory(0, count), cancellationToken);
                await input.CopyToAsync(output, cancellationToken);
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                throw;
            }

            return $"/{RelativeDirectory}/{fileName}";
        }

        public void Delete(string? imageUrl)
        {
            const string prefix = "/uploads/profiles/";
            if (string.IsNullOrWhiteSpace(imageUrl) ||
                !imageUrl.StartsWith(prefix, StringComparison.Ordinal)) return;

            string fileName = imageUrl[prefix.Length..];
            if (fileName.Length == 0 || fileName != Path.GetFileName(fileName)) return;

            string path = Path.Combine(_physicalDirectory, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
