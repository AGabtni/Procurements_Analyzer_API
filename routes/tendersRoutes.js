// Imports
const express = require('express');
const router = express.Router();
const pool = require('../app');

// Return open tenders list
router.get('/open', async (req, res) => {

  const { limit, offset } = req.query;

  // Ensure limit and offset are valid numbers
  const limitNumber = parseInt(limit);
  const offsetNumber = parseInt(offset);
  if (isNaN(limitNumber) || isNaN(offsetNumber)) {
    return res.status(400).json({ error: 'Invalid limit or offset values' });
  }

  try {

    // Validate that the offset is within table rows count
    const rowCountQuery = 'SELECT COUNT(*) FROM "Tenders"';
    const rowCountResult = await pool.query(rowCountQuery);
    const rowCount = parseInt(rowCountResult.rows[0].count, 10);

    if (offsetNumber >= rowCount) {
      return res.status(400).json({ error: 'Offset is out of range.' });
    }

    const query = {
      text: 'SELECT * FROM "Tenders" LIMIT $1 OFFSET $2',
      values: [limitNumber, offsetNumber],
    };

    const result = await pool.query(query);

    res.json(result.rows);
  } catch (error) {
    console.error('Error fetching data:', error);
    res.status(500).json({ error: 'An error occurred while fetching data.' });
  }
});

module.exports = router;