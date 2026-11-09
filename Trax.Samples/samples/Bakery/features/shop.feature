Feature: The shop
  A baker clocking in opens the shop, and clocking out closes it. Orders wait for the oven until a baker fires it:
  every waiting order goes in with the batch, and comes out ready to collect.

  Scenario: A baker clocking in opens the shop
    Given Kit is a baker
    And Kit has a shift
    And the shop is closed
    When Kit clocks in for his shift
    Then Kit's shift is on
    And the shop is open
    And Kit's shop sign shows open "We're open"

  Scenario: A baker clocking out closes the shop
    Given Kit is a baker
    And Kit has a shift
    And Kit's shift is on
    And the shop is open
    When Kit clocks out of his shift
    Then Kit's shift is over
    And the shop is closed

  Scenario: A customer reads the sign
    Given Ada is a customer
    And the shop is closed
    Then Ada's shop sign shows open "Closed"

  Scenario: A baker fires the oven, and every waiting order goes in
    Given Ada is a customer
    And Ada has an order
    And Ada's order is placed
    And Kit is a baker
    And Kit has a shift
    And Kit's shift is on
    And the shop is open
    When Kit fires the oven on his shift
    Then Kit's shift is at the oven
    And the shop is baking a batch
    And Ada's order is baking
    And Ada's orders show status "In the oven"
    And Kit's shop sign shows open "We're open, the oven is on"

  Scenario: A baker steps back from the oven
    Given Kit is a baker
    And Kit has a shift
    And Kit's shift is at the oven
    And the shop is baking a batch
    When 2 seconds pass
    Then Kit's shift is on
    And the shop is still baking a batch

  Scenario: A batch takes four seconds
    Given the shop is baking a batch
    When 4 seconds pass
    Then the shop is open

  Scenario: An order comes out of the oven
    Given Ada is a customer
    And Ada has an order
    And Ada's order is baking
    When 10 seconds pass
    Then Ada's order is ready to collect
    And Ada's orders show status "Ready to collect"

  Scenario: An order waits for the oven
    Given Ada is a customer
    And Ada has an order
    And Ada's order is placed
    When 10 seconds pass
    Then Ada's order is still placed

  Scenario: The oven fires
    Given the shop is open
    When the oven fires
    Then the shop is baking a batch

  Scenario: The shop opens
    Given the shop is closed
    When the shop opens
    Then the shop is open

  Scenario: Opening an open shop changes nothing
    Given the shop is open
    When the shop opens
    Then the shop is open

  Scenario: Opening the shop during a batch changes nothing
    Given the shop is baking a batch
    When the shop opens
    Then the shop is baking a batch

  Scenario: The shop closes
    Given the shop is open
    When the shop closes
    Then the shop is closed

  Scenario: Closing a closed shop changes nothing
    Given the shop is closed
    When the shop closes
    Then the shop is closed

  Scenario: The shop closes in the middle of a batch
    Given the shop is baking a batch
    When the shop closes
    Then the shop is closed

  Scenario: An order goes in the oven
    Given Ada is a customer
    And Ada has an order
    And Ada's order is placed
    When Ada's order starts baking
    Then Ada's order is baking
