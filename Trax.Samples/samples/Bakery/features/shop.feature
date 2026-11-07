Feature: The shop
  A baker clocking in opens the shop, the orders waiting for it go in the oven, and it closes after hours.

  Scenario: A baker clocking in opens the shop
    Given Kit is a baker
    And Kit has a shift
    And the shop is closed
    When Kit clocks in for his shift
    Then Kit's shift is on
    And the shop is open
    And Kit's shop sign shows open "We're open"

  Scenario: Waiting orders start baking when the shop opens
    Given Ada is a customer
    And Ada has an order
    And Ada's order is placed
    And Kit is a baker
    And Kit has a shift
    And the shop is closed
    When Kit clocks in for his shift
    Then Kit's shift is on
    And the shop is open
    And Ada's order is baking
    And Ada's orders show status "In the oven"

  Scenario: A customer reads the sign
    Given Ada is a customer
    And the shop is closed
    Then Ada's shop sign shows open "Closed"

  Scenario: The shop is still open before closing time
    Given the shop is open
    When 3 seconds pass
    Then the shop is still open

  Scenario: The shop closes after hours
    Given the shop is open
    When 5 seconds pass
    Then the shop is closed

  Scenario: The shop opens
    Given the shop is closed
    When the shop opens
    Then the shop is open

  Scenario: An order goes in the oven
    Given Ada is a customer
    And Ada has an order
    And Ada's order is placed
    When Ada's order starts baking
    Then Ada's order is baking
