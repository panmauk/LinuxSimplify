using System.Collections.Generic;
using System.Linq;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    public class CompatibilityAnalyzer
    {
        private readonly List<DistroRequirements> distroDatabase = new List<DistroRequirements>
        {
            new DistroRequirements
            {
                Name = "Ubuntu",
                MinRamGB = 4, RecommendedRamGB = 8, MinStorageGB = 25,
                SupportsProprietaryDrivers = true,
                GoodForGaming = true, GoodForBeginners = true,
                DownloadUrl = "https://releases.ubuntu.com/noble/ubuntu-24.04.1-desktop-amd64.iso",
                Sha256Url = "https://releases.ubuntu.com/noble/SHA256SUMS"
            },
            new DistroRequirements
            {
                Name = "Fedora",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 20,
                SupportsProprietaryDrivers = true,
                GoodForAdvanced = true,
                DownloadUrl = "https://mirror.arizona.edu/fedora/linux/releases/43/Workstation/x86_64/iso/Fedora-Workstation-Live-43-1.6.x86_64.iso",
                Sha256Url = "https://mirror.arizona.edu/fedora/linux/releases/43/Workstation/x86_64/iso/Fedora-Workstation-43-1.6-x86_64-CHECKSUM"
            },
            new DistroRequirements
            {
                Name = "Linux Mint",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 20,
                SupportsProprietaryDrivers = true,
                GoodForBeginners = true,
                DownloadUrl = "https://mirrors.edge.kernel.org/linuxmint/stable/22/linuxmint-22-cinnamon-64bit.iso",
                Sha256Url = "https://mirrors.edge.kernel.org/linuxmint/stable/22/sha256sum.txt"
            },
            new DistroRequirements
            {
                Name = "Debian",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 10,
                SupportsProprietaryDrivers = true,
                DownloadUrl = "https://cdimage.debian.org/debian-cd/current/amd64/iso-cd/debian-12.9.0-amd64-netinst.iso",
                Sha256Url = "https://cdimage.debian.org/debian-cd/current/amd64/iso-cd/SHA256SUMS"
            },
            new DistroRequirements
            {
                Name = "Arch Linux",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 20,
                SupportsProprietaryDrivers = true,
                GoodForAdvanced = true, GoodForGaming = true,
                DownloadUrl = "https://geo.mirror.pkgbuild.com/iso/latest/archlinux-x86_64.iso",
                Sha256Url = "https://geo.mirror.pkgbuild.com/iso/latest/sha256sums.txt"
            },
            new DistroRequirements
            {
                Name = "EndeavourOS",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 15,
                SupportsProprietaryDrivers = true,
                GoodForGaming = true, GoodForAdvanced = true,
                DownloadUrl = "https://mirror.alpix.eu/endeavouros/iso/EndeavourOS_Ganymede-Neo-2026.01.12.iso",
                Sha256Url = "https://mirror.alpix.eu/endeavouros/iso/EndeavourOS_Ganymede-Neo-2026.01.12.iso.sha512sum",
                ChecksumType = "sha512"
            },
            new DistroRequirements
            {
                Name = "Pop!_OS",
                MinRamGB = 4, RecommendedRamGB = 8, MinStorageGB = 20,
                SupportsProprietaryDrivers = true,
                GoodForGaming = true, GoodForBeginners = true,
                DownloadUrl = "https://iso.pop-os.org/22.04/amd64/intel/50/pop-os_22.04_amd64_intel_50.iso",
                NvidiaDownloadUrl = "https://iso.pop-os.org/22.04/amd64/nvidia/50/pop-os_22.04_amd64_nvidia_50.iso",
                Sha256Url = "https://iso.pop-os.org/22.04/amd64/intel/50/SHA256SUMS"
            },
            new DistroRequirements
            {
                Name = "Lubuntu",
                MinRamGB = 1, RecommendedRamGB = 2, MinStorageGB = 10,
                SupportsProprietaryDrivers = true,
                GoodForOldHardware = true, GoodForBeginners = true,
                DownloadUrl = "https://cdimage.ubuntu.com/lubuntu/releases/noble/release/lubuntu-24.04-desktop-amd64.iso",
                Sha256Url = "https://cdimage.ubuntu.com/lubuntu/releases/noble/release/SHA256SUMS"
            },
            new DistroRequirements
            {
                Name = "Zorin OS",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 15,
                SupportsProprietaryDrivers = true,
                GoodForBeginners = true,
                DownloadUrl = "https://mirrors.edge.kernel.org/zorinos-isos/17/Zorin-OS-17.3-Core-64-bit-r2.iso",
                Sha256Url = ""
            },
            new DistroRequirements
            {
                Name = "Trisquel",
                MinRamGB = 2, RecommendedRamGB = 4, MinStorageGB = 15,
                SupportsProprietaryDrivers = false,
                DownloadUrl = "https://mirror.fsf.org/trisquel-images/trisquel_11.0.1_amd64.iso",
                Sha256Url = "https://mirror.fsf.org/trisquel-images/trisquel_11.0.1_amd64.iso.sha256"
            }
        };

        public List<DistroCompatibility> AnalyzeCompatibility(HardwareInfo hw)
        {
            var all = new List<DistroCompatibility>();
            foreach (var distro in distroDatabase)
                all.Add(AnalyzeDistro(hw, distro));

            // Filter out incompatible distros entirely
            var compatible = all.Where(d => d.CompatibilityStatus != "Not Compatible").ToList();

            // Sort: recommended first, then compatible
            var recommended = compatible.Where(d => d.IsRecommended).ToList();
            var others = compatible.Where(d => !d.IsRecommended).ToList();

            var totalStorage = hw.StorageDevices.Sum(s => s.SizeGB);

            recommended.Sort((a, b) =>
            {
                int scoreA = DistroFitScore(a.Name, hw, totalStorage);
                int scoreB = DistroFitScore(b.Name, hw, totalStorage);
                return scoreB.CompareTo(scoreA);
            });

            others.Sort((a, b) =>
            {
                int scoreA = DistroFitScore(a.Name, hw, totalStorage);
                int scoreB = DistroFitScore(b.Name, hw, totalStorage);
                return scoreB.CompareTo(scoreA);
            });

            var result = new List<DistroCompatibility>();
            result.AddRange(recommended);
            result.AddRange(others);
            return result;
        }

        private int DistroFitScore(string name, HardwareInfo hw, double totalStorage)
        {
            int score = 0;
            bool isLight = name == "Lubuntu" || name == "Trisquel";
            bool isHeavy = name == "Fedora" || name == "Zorin OS" || name == "Pop!_OS" || name == "Ubuntu";
            bool isGaming = name == "Pop!_OS" || name == "EndeavourOS" || name == "Arch Linux";

            if (hw.RamGB <= 4) { if (isLight) score += 20; if (isHeavy) score -= 10; }
            else if (hw.RamGB >= 16) { if (isHeavy) score += 5; }

            if (totalStorage < 64) { if (isLight) score += 15; if (isHeavy) score -= 5; }

            if (hw.Gpus.Any(g => g.IsGaming)) { if (isGaming) score += 10; }
            if (hw.Gpus.Any(g => g.IsWorkstation)) { if (name == "Fedora" || name == "Ubuntu") score += 10; }

            if (hw.Profile == SystemProfile.Standard)
            {
                var distro = distroDatabase.FirstOrDefault(d => d.Name == name);
                if (distro != null && distro.GoodForBeginners) score += 8;
            }

            return score;
        }

        public List<DistroCompatibility> GetAllDistrosWithoutHardware()
        {
            var results = new List<DistroCompatibility>();
            foreach (var distro in distroDatabase)
            {
                results.Add(new DistroCompatibility
                {
                    Name = distro.Name,
                    CompatibilityStatus = "Unknown",
                    Notes = "Scan hardware first",
                    IsRecommended = false,
                    DownloadUrl = distro.DownloadUrl,
                    Sha256Url = distro.Sha256Url,
                    ChecksumType = distro.ChecksumType,
                    IsSelected = false
                });
            }
            return results;
        }

        private DistroCompatibility AnalyzeDistro(HardwareInfo hw, DistroRequirements distro)
        {
            var notes = new List<string>();
            bool isCompatible = true;
            bool isRecommended = false;

            // ===== GPU analysis =====
            bool hasNvidia = hw.Gpus.Any(g => g.Vendor == "NVIDIA");
            bool hasAmdDiscrete = hw.Gpus.Any(g => g.Vendor == "AMD" && g.IsDiscrete);
            bool hasGamingGpu = hw.Gpus.Any(g => g.IsGaming);
            bool hasHighEndGpu = hw.Gpus.Any(g => g.Tier >= GpuTier.HighEnd);
            bool hasMidRangeOrBetter = hw.Gpus.Any(g => g.Tier >= GpuTier.MidRange);
            bool hasWorkstationGpu = hw.Gpus.Any(g => g.IsWorkstation);
            bool integratedOnly = hw.Gpus.All(g => !g.IsDiscrete);
            var bestGpu = hw.Gpus.OrderByDescending(g => (int)g.Tier).FirstOrDefault();

            // NVIDIA + no proprietary driver support = hard incompatible
            if (hasNvidia && !distro.SupportsProprietaryDrivers)
            {
                isCompatible = false;
                string gpuName = hw.Gpus.FirstOrDefault(g => g.Vendor == "NVIDIA")?.Model ?? "NVIDIA GPU";
                notes.Add($"Your {ShortGpuName(gpuName)} needs proprietary drivers");
            }

            // ===== RAM =====
            if (hw.RamGB < distro.MinRamGB)
            {
                isCompatible = false;
                notes.Add($"Needs at least {distro.MinRamGB} GB RAM");
            }
            else if (hw.RamGB < distro.RecommendedRamGB)
            {
                notes.Add($"Works, but {distro.RecommendedRamGB} GB RAM recommended");
            }

            // ===== Storage =====
            var totalStorage = hw.StorageDevices.Sum(s => s.SizeGB);
            if (totalStorage < distro.MinStorageGB)
            {
                isCompatible = false;
                notes.Add($"Needs {distro.MinStorageGB} GB disk space");
            }

            // ===== Download URL =====
            string downloadUrl = distro.DownloadUrl;
            if (hasNvidia && !string.IsNullOrEmpty(distro.NvidiaDownloadUrl))
            {
                downloadUrl = distro.NvidiaDownloadUrl;
                notes.Add("NVIDIA edition with drivers included");
            }

            // ===== RECOMMENDATION ENGINE =====
            if (!isCompatible) goto Finish;

            bool meetsRec = hw.RamGB >= distro.RecommendedRamGB;
            var profile = hw.Profile;
            string n = distro.Name;

            if (n == "Pop!_OS")
            {
                if (hasNvidia && hasGamingGpu)
                {
                    isRecommended = true;
                    if (hasHighEndGpu)
                        notes.Add($"Best pick for your {ShortGpuName(bestGpu.Model)} — drivers preinstalled");
                    else
                        notes.Add("NVIDIA drivers work right out of the box");
                }
                else if (hasNvidia)
                {
                    isRecommended = true;
                    notes.Add("Great NVIDIA support out of the box");
                }
                else if (meetsRec)
                    notes.Add("Clean, productivity-focused desktop");
            }
            else if (n == "Ubuntu")
            {
                if (profile == SystemProfile.GamingDesktop && hasNvidia && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Large gaming community, easy NVIDIA driver install");
                }
                else if (profile == SystemProfile.Standard && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Great all-rounder, biggest community");
                }
                else if (meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Most popular distro, huge community");
                }
                else
                    notes.Add("Popular, well-supported");
            }
            else if (n == "Linux Mint")
            {
                if (profile == SystemProfile.Standard && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Easiest transition from Windows");
                }
                else if (meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Beginner-friendly, Windows-like feel");
                }
                else
                    notes.Add("Very approachable for newcomers");
            }
            else if (n == "Fedora")
            {
                if (profile == SystemProfile.Workstation && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Great match for workstation hardware");
                }
                else if (hasAmdDiscrete && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Excellent AMD GPU support with latest drivers");
                }
                else if (meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Cutting-edge packages, great for developers");
                }
                else
                    notes.Add("Backed by Red Hat, latest software");
            }
            else if (n == "Arch Linux")
            {
                if (profile == SystemProfile.GamingDesktop && hasMidRangeOrBetter)
                    notes.Add($"Rolling release = latest drivers for your {ShortGpuName(bestGpu.Model)}");
                else
                    notes.Add("For advanced users, full control over everything");
            }
            else if (n == "EndeavourOS")
            {
                if (profile == SystemProfile.GamingDesktop)
                {
                    isRecommended = true;
                    notes.Add("Arch-based with latest drivers, easier to set up");
                }
                else if (hasMidRangeOrBetter && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Rolling release, always latest drivers");
                }
                else if (meetsRec)
                    notes.Add("User-friendly way into Arch");
                else
                    notes.Add("Arch-based, good community");
            }
            else if (n == "Lubuntu")
            {
                if (profile == SystemProfile.Lightweight || profile == SystemProfile.Legacy)
                {
                    isRecommended = true;
                    notes.Add("Perfect for your hardware — runs fast with minimal resources");
                }
                else if (hw.RamGB <= 4)
                {
                    isRecommended = true;
                    notes.Add("Best choice for low-RAM systems");
                }
                else if (integratedOnly && hw.RamGB <= 8)
                    notes.Add("Lightweight, won't tax your integrated graphics");
                else
                    notes.Add("Ultra-lightweight desktop");
            }
            else if (n == "Zorin OS")
            {
                if (profile == SystemProfile.Standard && meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Looks and feels like Windows, smooth transition");
                }
                else if (meetsRec)
                {
                    isRecommended = true;
                    notes.Add("Designed for Windows users switching to Linux");
                }
                else
                    notes.Add("Familiar Windows-like interface");
            }
            else if (n == "Debian")
            {
                if (profile == SystemProfile.Workstation)
                    notes.Add("Rock-solid stability for production work");
                else
                    notes.Add("Very stable, conservative updates");
            }
            else if (n == "Trisquel")
            {
                if (integratedOnly && hw.Gpus.All(g => g.Vendor == "Intel"))
                    notes.Add("Your Intel GPU works great with free drivers");
                else if (hasAmdDiscrete)
                    notes.Add("AMD GPUs have good open-source drivers");
                else
                    notes.Add("100% free software, no proprietary drivers");
            }

            Finish:
            if (isCompatible && notes.Count == 0)
                notes.Add("Meets all requirements");

            string statusText;
            if (!isCompatible)
                statusText = "Not Compatible";
            else if (isRecommended)
                statusText = "Recommended";
            else
                statusText = "Compatible";

            return new DistroCompatibility
            {
                Name = distro.Name,
                CompatibilityStatus = statusText,
                Notes = string.Join("; ", notes),
                IsRecommended = isCompatible && isRecommended,
                DownloadUrl = downloadUrl,
                Sha256Url = distro.Sha256Url,
                ChecksumType = distro.ChecksumType,
                IsSelected = false
            };
        }

        private string ShortGpuName(string fullModel)
        {
            if (string.IsNullOrEmpty(fullModel)) return "GPU";
            string m = fullModel;
            foreach (var prefix in new[] { "NVIDIA ", "AMD ", "Intel ", "GeForce ", "Radeon " })
                m = m.Replace(prefix, "");
            m = m.Trim();
            return string.IsNullOrEmpty(m) ? "GPU" : m;
        }
    }
}
