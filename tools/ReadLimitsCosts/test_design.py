import unittest
from pathlib import Path
from paired import cases


class DesignControls(unittest.TestCase):
    def test_inventory(self):
        self.assertEqual(len(cases('component')), 9)
        self.assertEqual(len(cases('copy')), 20)
        for group in ('component', 'copy'):
            rows = cases(group)
            self.assertEqual(len(rows), len({row[0] for row in rows}))

    def test_guard_comparison_is_within_candidate(self):
        for name, left, right in cases('component'):
            if name.startswith('GuardDiagnostic'):
                self.assertEqual((left[0], right[0]), ('B', 'B'))
                self.assertTrue(left[1].startswith('GuardNoop'))
                self.assertTrue(right[1].startswith('GuardAccepted'))
            else:
                self.assertEqual((left[0], right[0]), ('A', 'B'))
                self.assertEqual(left[1], right[1])

    def test_copy_revision_comparisons_match_names(self):
        for name, left, right in cases('copy'):
            self.assertEqual(left, ('A', name))
            self.assertEqual(right, ('B', name))

    def test_balanced_order(self):
        orders = ['AB' if i % 2 == 0 else 'BA' for i in range(8)]
        self.assertEqual(orders.count('AB'), 4)
        self.assertEqual(orders.count('BA'), 4)

    def test_observation_and_copy_controls(self):
        source = (Path(__file__).parent / 'Program.cs').read_text()
        self.assertLess(source.index('Console.WriteLine("RESULT '), source.index('Console.WriteLine("OBS '))
        self.assertIn('retained.Clear();', source)
        self.assertIn('GC.AllocateUninitializedArray<byte>(length)', source)
        self.assertIn('Unsafe.CopyBlockUnaligned', source)
        self.assertIn('ReferenceEquals(holder.Value, expected)', source)


if __name__ == '__main__': unittest.main()
