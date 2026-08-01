class Scrobbler < Formula
  desc "Linux Last.fm scrobbler: MPRIS daemon (scrbl) + CLI (scrbl-cli)"
  homepage "https://github.com/Avabin/Scrobbler"
  url "https://github.com/Avabin/Scrobbler.git", using: :git, tag: "v0.1.0", revision: "253bd83a3d78b78ab7814d28f43e69f77122f487"
  license "MIT"

  depends_on "brotli" => :build
  depends_on "dotnet" => :build
  depends_on "llvm" => :build
  depends_on "zlib" => :build

  def install
    system "dotnet", "publish", "Scrobbler/Scrobbler.csproj",
           "-c", "Release",
           "-r", "linux-x64",
           "--self-contained", "true",
           "--output", "publish-daemon"
    libexec.install Dir["publish-daemon/*"]
    bin.write_exec_script libexec/"scrbl"

    ENV.prepend_path "PATH", formula_opt_bin("llvm")
    ENV["LIBRARY_PATH"] = [formula_opt_lib("brotli"), formula_opt_lib("zlib")].join(":")

    system "dotnet", "publish", "Scrobbler.Cli/Scrobbler.Cli.csproj",
           "-c", "Release",
           "-r", "linux-x64",
           "--output", "publish-cli"
    bin.install "publish-cli/scrbl-cli"
  end

  service do
    run [opt_bin/"scrbl"]
    environment_variables DOTNET_ENVIRONMENT: "Production"
    run_type :immediate
    keep_alive true
  end

  test do
    assert_match "auth", shell_output("#{bin}/scrbl-cli --help")
  end
end
