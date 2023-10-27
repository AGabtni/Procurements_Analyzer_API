// Imports
const express = require('express');
const router = express.Router();
const pool = require('../app');
const { DateTime } = require('luxon');

// Return open tenders list
router.get('/open', async (req, res) => {

  const { limit, offset } = req.query;

  // Ensure limit and offset are valid numbers
  var limitNumber = parseInt(limit);
  var offsetNumber = parseInt(offset);

  //Default parameters if they are invalid
  if (isNaN(limitNumber))
    limitNumber = 200;
  if (isNaN(offsetNumber))
    offsetNumber = 0;


  try {

    // Validate that the offset is within table rows count
    const rowCountQuery = 'SELECT COUNT(*) FROM "Tenders"';
    const rowCountResult = await pool.query(rowCountQuery);
    const rowCount = parseInt(rowCountResult.rows[0].count, 10);

    if (offsetNumber >= rowCount) {
      return res.status(400).json({ error: 'Offset is out of range.' });
    }

    const query = {
      text: 'SELECT * FROM "Tenders" WHERE closing_date > extract(epoch from now()) ORDER BY pub_date DESC LIMIT $1 OFFSET $2',
      values: [limitNumber, offsetNumber],
    };

    const result = await pool.query(query);

    const formattedRows = result.rows.map(row => {

      var pubDate = row.pub_date
      var closingDate = row.closing_date

      // Format publication date from timestamp
      if (row.pub_date) 
        pubDate = DateTime.fromSeconds(parseInt(row.pub_date.toFixed(0)));
      
      // Format closing date from timestamp
      if(row.closing_date)
        closingDate = DateTime.fromSeconds(parseInt(row.closing_date.toFixed(0)));


      return { id: row.nt_id,
              title: row.nt_title,
              category: row.proc_cat,
              buyer_org : row.buying_org,
              publication_date: pubDate, 
              closing_date: closingDate,
              bid_type : row.nt_type,
              procurement_method : row.proc_method,
              selection_criteria : row.sel_criteria,
              link : row.ext_link.length > 0 ?  row.ext_link  :  row.nt_link,
              unspsc : row.unspsc,
              gsin : row.gsin,
      };

    });
    res.json(formattedRows);
  } catch (error) {
    console.error('Error fetching data:', error);
    res.status(500).json({ error: 'An error occurred while fetching data.' });
  }
});

module.exports = router;