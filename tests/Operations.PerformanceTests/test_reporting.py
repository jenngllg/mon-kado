import unittest

from reporting import resources


class ReportingTests(unittest.TestCase):
    def test_resource_summary_excludes_warmup_cpu_and_keeps_lifetime_memory_peak(self):
        self.assertIsNone(resources([])["meanMeasuredCpuPercentOfOneCore"])
        samples = [{"phase": "warmup", "memoryPeak": 90},
                   {"phase": "measure", "memoryPeak": 100, "observedAt": "2026-09-22T00:00:00+00:00", "cpu": {"usage_usec": "1000000"}},
                   {"phase": "measure", "memoryPeak": 200, "observedAt": "2026-09-22T00:00:10+00:00", "cpu": {"usage_usec": "6000000"}}]
        summary = resources(samples)
        self.assertEqual(50, summary["meanMeasuredCpuPercentOfOneCore"])
        self.assertEqual(200, summary["memoryPeakBytesIncludingSetup"])
        self.assertIsNone(resources([samples[1], samples[1]])["meanMeasuredCpuPercentOfOneCore"])
