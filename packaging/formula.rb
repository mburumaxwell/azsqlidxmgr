# typed: false
# frozen_string_literal: true

class Azsqlidxmgr < Formula
  desc "Azure SQL Index Manager"
  homepage "https://github.com/mburumaxwell/azsqlidxmgr"
  license "MIT"
  version "#{VERSION}#"

  on_linux do
    if Hardware::CPU.arm?
      url "https://github.com/mburumaxwell/azsqlidxmgr/releases/download/#{VERSION}#/azsqlidxmgr-#{VERSION}#-linux-arm64.tar.gz"
      sha256 "#{RELEASE_SHA256_LINUX_ARM64}#"
    end

    if Hardware::CPU.intel?
      url "https://github.com/mburumaxwell/azsqlidxmgr/releases/download/#{VERSION}#/azsqlidxmgr-#{VERSION}#-linux-x64.tar.gz"
      sha256 "#{RELEASE_SHA256_LINUX_X64}#"
    end
  end

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/mburumaxwell/azsqlidxmgr/releases/download/#{VERSION}#/azsqlidxmgr-#{VERSION}#-osx-arm64.tar.gz"
      sha256 "#{RELEASE_SHA256_MACOS_ARM64}#"
    end

    if Hardware::CPU.intel?
      url "https://github.com/mburumaxwell/azsqlidxmgr/releases/download/#{VERSION}#/azsqlidxmgr-#{VERSION}#-osx-x64.tar.gz"
      sha256 "#{RELEASE_SHA256_MACOS_X64}#"
    end
  end

  def install
    bin.install "azsqlidxmgr"
  end

  test do
    system "#{bin}/azsqlidxmgr", "--version"
  end
end
