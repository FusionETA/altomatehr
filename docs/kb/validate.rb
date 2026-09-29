# Validates every entry in docs/kb before it reaches the support assistant.
# A malformed header makes a retrieval pipeline silently skip that entry.
# Run from docs/kb:  LC_ALL=en_US.UTF-8 ruby -E UTF-8 validate.rb

require "yaml"; require "date"
req = %w[id title module audience kind status severity escalate verified symptoms keywords related internal_ref]
files = Dir["*.md"].sort - ["README.md"]
ids = {}; bad = []
files.each do |f|
  s = File.read(f)
  m = s.match(/\A---\n(.*?)\n---\n(.*)\z/m)
  unless m then bad << "#{f}: no frontmatter"; next end
  begin
    d = YAML.safe_load(m[1], permitted_classes: [Date])
  rescue => e
    bad << "#{f}: YAML ERROR #{e.message.lines.first.strip}"; next
  end
  ids[f] = d
  next if f == "_assistant-rules.md"
  bad << "#{f}: id #{d['id'].inspect} != filename" if d["id"] != f.sub(/\.md\z/, "")
  miss = req - d.keys; bad << "#{f}: missing #{miss}" unless miss.empty?
  syms = d["symptoms"] || []
  bad << "#{f}: only #{syms.size} symptoms" if syms.size < 4
  bad << "#{f}: no Malay symptom" unless syms.any? { |x| x =~ /\b(saya|tak|tidak|tiada|kenapa|macam|pekerja|cuti|tuntutan|gaji|fail|terlalu|swafoto|waktu|potongan|butang)\b/i }
end
all = ids.values.map { |d| d["id"] }
ids.each { |f, d| (d["related"] || []).each { |r| bad << "#{f}: related #{r.inspect} missing" unless all.include?(r) } }
puts "files: #{files.size}"
puts(bad.empty? ? "ALL CHECKS PASS" : bad)
