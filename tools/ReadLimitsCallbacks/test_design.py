import unittest
from pathlib import Path
from paired import CASES, ORDERS, CASE_ORDERS, paired_ratios, interaction


class DesignTests(unittest.TestCase):
    def test_inventory_and_balanced_order(self):
        self.assertEqual(len(CASES), 4)
        self.assertEqual(len(set(CASES)), 4)
        for a, b in (('A', 'B'), ('A', 'C'), ('B', 'C')):
            self.assertEqual(sum(order.index(a) < order.index(b) for order in ORDERS), 4)
        for case in range(4):
            for position in range(4):
                self.assertEqual(sum(order[position] == case for order in CASE_ORDERS), 2)

    def test_all_runs_and_controls_retained(self):
        keys = [side + str(block) for block in range(8) for side in ORDERS[block]]
        keys += [f'control{side}{block}-{repeat}' for block in (2, 6) for side in 'BC' for repeat in (1, 2)]
        self.assertEqual(len(keys), 32)
        self.assertEqual(len(set(keys)), 32)

    def test_paired_shape_interaction(self):
        def item(c):
            return {'runs': {side + str(i): {'samples': [{'ns': value}] * 5}
                for i in range(8) for side, value in [('A', 8), ('B', 10), ('C', c)]}}
        data = {'shared': item(13), 'separate': item(10)}
        self.assertEqual(paired_ratios(data, 'shared', 'C', 'B'), [1.3] * 8)
        self.assertEqual(interaction(data, 'shared', 'separate', 'C', 'B'), [1.3] * 8)

    def test_snapshots_outside_sample_loop(self):
        source = (Path(__file__).parent / 'Program.cs').read_text()
        self.assertLess(source.index('long jitBytesBefore = JitDumpLength();'), source.index('for (int sample = 0;'))
        self.assertGreater(source.index('long jitBytesAfter = JitDumpLength();'), source.index('samples.Add(new'))
        self.assertEqual(source.count('= JitDumpLength();'), 2)

    def test_separate_callbacks_have_no_context_selection(self):
        source = (Path(__file__).parent / 'Program.cs').read_text()
        self.assertIn('int ReadInt() => context == null ?', source)
        self.assertIn('int ReadRawInt() => SharpPackSerializer.Deserialize<int>(primitive.Span);', source)
        self.assertIn('int ReadContextInt() => SharpPackSerializer.Deserialize<int>(primitive.Span, context!);', source)
        self.assertIn('References32', source)


if __name__ == '__main__': unittest.main()
