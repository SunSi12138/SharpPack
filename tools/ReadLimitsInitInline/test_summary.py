import unittest
from run import interval, med, records

class SummaryTests(unittest.TestCase):
    def test_identity(self):
        self.assertEqual(interval([1.0] * 8), (1.0, 1.0, 1.0))
    def test_known_ratio(self):
        point, low, high = interval([1.25] * 8)
        self.assertAlmostEqual(point, 1.25)
        self.assertAlmostEqual(low, 1.25)
        self.assertAlmostEqual(high, 1.25)
    def test_keeps_outlier(self):
        point, low, high = interval([1] * 7 + [2])
        self.assertGreater(point, 1.08)
        self.assertGreater(high, point)
        self.assertEqual(low, 1)
    def test_rejects_nonfinite(self):
        with self.assertRaises(RuntimeError): interval([float('nan')])
    def test_rejects_duplicate(self):
        with self.assertRaises(RuntimeError): records('RESULT {"Name":"a"}\nRESULT {"Name":"a"}', 'RESULT')
    def test_rejects_missing(self):
        with self.assertRaises(RuntimeError): records('no rows', 'RESULT')
    def test_rejects_short_samples(self):
        with self.assertRaises(RuntimeError): med({'samples': [{'ns': 1}]}, 'ns')
    def test_median_preserves_all_samples(self):
        self.assertEqual(med({'samples': [{'ns': x} for x in [1,2,3,4,100]]}, 'ns'), 3)

if __name__ == '__main__': unittest.main()
